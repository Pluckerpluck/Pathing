﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BhModule.Community.Pathing.Content;
using BhModule.Community.Pathing.Entity;
using BhModule.Community.Pathing.Entity.Effects;
using BhModule.Community.Pathing.State;
using BhModule.Community.Pathing.Utility;
using Blish_HUD;
using Microsoft.Xna.Framework;
using TmfLib;
using TmfLib.Pathable;
using TmfLib.Prototype;

namespace BhModule.Community.Pathing {
    public class SharedPackState : IRootPackState {
        
        public ModuleSettings UserConfiguration { get; }

        public int CurrentMapId { get; set; }

        public PathingCategory RootCategory { get; private set; }

        public MarkerEffect SharedMarkerEffect { get; private set; }
        public TrailEffect  SharedTrailEffect  { get; private set; }

        public BehaviorStates     BehaviorStates     { get; private set; }
        public AchievementStates  AchievementStates  { get; private set; }
        public CategoryStates     CategoryStates     { get; private set; }
        public MapStates          MapStates          { get; private set; }
        public UserResourceStates UserResourceStates { get; private set; }
        public UiStates           UiStates           { get; private set; }
        public EditorStates       EditorStates       { get; private set; }

        public  SafeList<IPathingEntity> Entities { get; private set; } = new();

        private bool _initialized = false;
        private bool _loadingPack = false;

        public SharedPackState(ModuleSettings moduleSettings) {
            this.UserConfiguration = moduleSettings;

            InitShaders();

            Blish_HUD.Common.Gw2.KeyBindings.Interact.Activated += OnInteractPressed;
        }

        private ManagedState[] _managedStates;

        private async Task ReloadStates() {
            await Task.WhenAll(_managedStates.Select(state => state.Reload()));
        }

        private async Task InitStates() {
            _managedStates = new[] {
                await (this.CategoryStates     = new CategoryStates(this)).Start(),
                await (this.AchievementStates  = new AchievementStates(this)).Start(),
                await (this.BehaviorStates     = new BehaviorStates(this)).Start(),
                await (this.MapStates          = new MapStates(this)).Start(),
                await (this.UserResourceStates = new UserResourceStates(this)).Start(),
                await (this.UiStates           = new UiStates(this)).Start(),
                await (this.EditorStates       = new EditorStates(this)).Start()
            };

            _initialized = true;
        }

        private IPathingEntity BuildEntity(IPointOfInterest pointOfInterest) {
            return pointOfInterest.Type switch {
                PointOfInterestType.Marker => new StandardMarker(this, pointOfInterest),
                PointOfInterestType.Trail => new StandardTrail(this, pointOfInterest as ITrail),
                PointOfInterestType.Route => throw new NotImplementedException("Routes have not been implemented."),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        public async Task LoadPackCollection(IPackCollection collection) {
            // TODO: Support cancel instead of spinning like this.
            while (_loadingPack) {
                await Task.Delay(100);
            }

            _loadingPack = true;

            this.RootCategory = collection.Categories;

            if (!_initialized) {
                await InitStates();
            } else {
                await ReloadStates();
            }

            await InitPointsOfInterest(collection.PointsOfInterest);

            _loadingPack = false;
        }

        private static async Task PreloadTextures(IPointOfInterest pointOfInterest) {
            string texture = pointOfInterest.Type switch {
                PointOfInterestType.Marker => pointOfInterest.GetAggregatedAttributeValue("iconfile"),
                PointOfInterestType.Trail => pointOfInterest.GetAggregatedAttributeValue("texture")
            };

            if (texture != null) {
                await TextureResourceManager.GetTextureResourceManager(pointOfInterest.ResourceManager).PreloadTexture(texture);
            }
        }

        private async Task InitPointsOfInterest(IList<PointOfInterest> pois) {
            var poiBag = new ConcurrentBag<IPathingEntity>();

            await pois.AsParallel().ParallelForEachAsync(PreloadTextures, Environment.ProcessorCount);

            pois.AsParallel()
                .Select(BuildEntity)
                .ForAll(poiBag.Add);

            this.Entities.AddRange(poiBag);
            GameService.Graphics.World.AddEntities(poiBag);

            await Task.CompletedTask;
        }

        private void InitShaders() {
            this.SharedMarkerEffect = new MarkerEffect(PathingModule.Instance.ContentsManager.GetEffect(@"hlsl\marker.mgfx"));
            this.SharedTrailEffect  = new TrailEffect(PathingModule.Instance.ContentsManager.GetEffect(@"hlsl\trail.mgfx"));

            this.SharedMarkerEffect.FadeTexture = PathingModule.Instance.ContentsManager.GetTexture(@"png\42975.png");
        }

        private void OnInteractPressed(object sender, EventArgs e) {
            // TODO: OnInteractPressed needs a better place.
            foreach (var entity in this.Entities) {
                if (entity is StandardMarker {Focused: true} marker) {
                    marker.Interact(false);
                }
            }
        }

        public void Update(GameTime gameTime) {
            if (_managedStates == null) return;

            foreach (var state in _managedStates) {
                state.Update(gameTime);
            }
        }

        public async Task Unload() {
            foreach (var pathingEntity in this.Entities) {
                pathingEntity.Unload();
            }

            GameService.Graphics.World.RemoveEntities(this.Entities);

            this.Entities = new SafeList<IPathingEntity>();

            this.RootCategory = null;

            await TextureResourceManager.UnloadAsync();
        }

    }
}
