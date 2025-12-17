using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared.Helpers;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace IncursionHelper
{
    public enum PedestalState { NotActivated, Queued, Activated }

    public class PedestalInfo
    {
        public Entity Entity;
        public int Number;
        public long ActivatedSeq;
        public long QueuePosition;
        public PedestalState State;
        public Vector2 ScreenPosition;

        public void Update(Camera camera)
        {
            if (Entity == null || !Entity.IsValid) return;

            ActivatedSeq = 0;
            QueuePosition = 0;
            var stateMachine = Entity.GetComponent<StateMachine>();
            if (stateMachine?.States != null)
            {
                foreach (var state in stateMachine.States)
                {
                    if (state.Name == "activated") ActivatedSeq = state.Value;
                    if (state.Name == "queued") QueuePosition = state.Value;
                }
            }

            State = ActivatedSeq > 0 ? PedestalState.Activated :
                    QueuePosition > 0 ? PedestalState.Queued : 
                    PedestalState.NotActivated;

            ScreenPosition = camera.WorldToScreen(Entity.Pos);
        }
    }

    public class RewardInfo
    {
        public string Title;
        public string Description;
    }

    public class TrackedReward
    {
        public Entity Entity;
        public RewardInfo Info;
    }

    public class TileOverlayInfo
    {
        public string Id;
        public string Text;
        public Color Color;
        public bool Destabilizes;
        public bool IsUpgradeTarget;
    }

    public class IncursionHelper : BaseSettingsPlugin<Settings>
    {
        #region Constants & Fields
        private const string PEDESTAL_METADATA = "Metadata/MiscellaneousObjects/LeagueIncursionNew/IncursionPedestalCrystal_";
        private const string ENCOUNTER_METADATA = "IncursionPedestalEncounter";
        private const string BENCH_PREFIX = "Metadata/MiscellaneousObjects/LeagueIncursionNew/IncursionBench";

        private readonly List<PedestalInfo> _pedestals = new();
        private readonly List<TrackedReward> _rewards = new();
        private Entity _altarEntity;

        private Dictionary<long, TileOverlayInfo> _cachedTileOverlays = new();
        private DateTime _lastTempleUpdate = DateTime.MinValue;
        private readonly TimeSpan _templeUpdateInterval = TimeSpan.FromSeconds(1);

        private static readonly Color[] _upgradePalette = new[]
        {
            Color.LightGreen,
            Color.Cyan,
            Color.Yellow,
            Color.Magenta,
            Color.Orange,
            Color.Pink
        };
        #endregion

        #region Mappings
        private static readonly Dictionary<string, RewardInfo> _rewardMapping = new()
        {
            { "ExtractAllSocketables", new RewardInfo { Title = "Extraction", Description = "Return Augments (Destroy Item)" } },
            { "WeaponOrArmourQualityLow", new RewardInfo { Title = "Forging Bench", Description = "Improve Quality" } },
            { "WeaponOrArmourQualityMid", new RewardInfo { Title = "Forging Bench", Description = "Greatly Improve Quality" } },
            { "WeaponOrArmourQualityHigh", new RewardInfo { Title = "Masterwork Forge", Description = "Quality >20% (Risk Corrupt)" } },
            { "WeaponOrArmourQualityHighMultiple", new RewardInfo { Title = "Triumphant Forge", Description = "Quality >20% (Risk Corrupt)" } },
            { "GemQualityLow", new RewardInfo { Title = "Gemcutter", Description = "Improve Gem Quality" } },
            { "GemQualityMid", new RewardInfo { Title = "Gemcutter", Description = "Greatly Improve Gem Quality" } },
            { "DoubleCorruptGem", new RewardInfo { Title = "Gem Corrupter", Description = "Modify/Destroy Corrupted Gem" } },
            { "CorruptTablet", new RewardInfo { Title = "Precursor Machine", Description = "Modify Tablet (Chance Destroy)" } },
            { "MutateUnique", new RewardInfo { Title = "Morphology", Description = "Reroll Corrupted Unique Mods" } },
            { "ModifySoulcore", new RewardInfo { Title = "Soul Core Infuser", Description = "Modify Soul Core (Chance Destroy)" } },
            { "ModifySoulcoreAlchemyLab", new RewardInfo { Title = "Alchemy Bench", Description = "Normal/Magic -> Rare" } },
            { "Doctor", new RewardInfo { Title = "Corruption Altar", Description = "Corrupt Item (Unpredictable)" } },
            { "CampaignCurrency", new RewardInfo { Title = "Exalted Bench", Description = "Augment Rare Item" } },
            { "Regal", new RewardInfo { Title = "Regal Bench", Description = "Magic -> Rare" } }
        };

        private static readonly Dictionary<string, (string Text, Color Color)> _roomNameMapping = new()
        {
            { "Crimson Hall", ("Corrupt", Color.Red) },
            { "Catalyst of Corruption", ("Corrupt", Color.Red) },
            { "Locus of Corruption", ("Corrupt", Color.Red) },
            { "Thaumaturge's Laboratory", ("Gem Corrupt", Color.Red) },
            { "Thaumaturge's Cuttery", ("Gem Corrupt", Color.Red) },
            { "Thaumaturge's Cathedral", ("Gem Corrupt", Color.Red) },
            { "Chamber of Souls", ("Soul Core Corrupt", Color.Red) },
            { "Core Machinarium", ("Soul Core Corrupt", Color.Red) },
            { "Grand Phylactory", ("Soul Core Corrupt", Color.Red) },
            { "Tablet Research Vault", ("Tablet Corrupt", Color.Red) },
            { "Altar of Sacrifice", ("Unique Item", Color.Orange) },
            { "Hall of Offerings", ("Unique Item", Color.Orange) },
            { "Apex of Oblation", ("Unique Item", Color.Orange) },
            { "Ancient Reliquary Vault", ("Unique Item", Color.Orange) },
            { "Kishara's Vault", ("Currency", Color.Gold) },
            { "Jiquani's Vault", ("High Lvl Rune", Color.Cyan) },
            { "Vault of Reverence", ("Lineage Gem", Color.Cyan) },
            { "Commander's Chamber", ("Uromoti", Color.Wheat) },
            { "Commander's Hall", ("Uromoti", Color.Wheat) },
            { "Commander's Headquarters", ("Uromoti", Color.Wheat) },
            { "Spymaster's Study", ("Juatalotli", Color.Wheat) },
            { "Hall of Shadows", ("Juatalotli", Color.Wheat) },
            { "Omnipresent Panopticon", ("Juatalotli", Color.Wheat) },
            { "Workshop", ("Quipolatl", Color.Wheat) },
            { "Automaton Lab", ("Quipolatl", Color.Wheat) },
            { "Stone Legion", ("Quipolatl", Color.Wheat) },
            { "Architect's Chamber", ("Xopec/Azcapa", Color.Wheat) },
            { "Chamber of Iron", ("Quality Bench", Color.LightGray) },
            { "Golden Forge", ("Quality Bench", Color.LightGray) },
            { "Dynamo", ("Power/Bench", Color.LightGray) },
            { "Shrine of Empowerment", ("Power/Bench", Color.LightGray) },
            { "Solar Nexus", ("Power/Bench", Color.LightGray) },
            { "Surgeon's Ward", ("Limb Mod", Color.LightGray) },
            { "Surgeon's Theatre", ("Limb Mod", Color.LightGray) },
            { "Surgeon's Symphony", ("Limb Mod", Color.LightGray) },
            { "Extraction Chamber", ("Extract Augments", Color.LightGray) },
            { "Royal Access Chamber", ("Access Atziri", Color.Magenta) },
            { "Atziri's Chamber", ("Atziri", Color.Magenta) },
            { "Sacrifice Room", ("Sacrifice Room", Color.Magenta) },

            { "Path", ("Path", Color.LightGray) },
            { "Guardhouse", ("Guardhouse", Color.LightGray) },
            { "Barracks", ("Barracks", Color.LightGray) },
            { "Hall of War", ("Hall of War", Color.LightGray) },
            { "Depot", ("Depot", Color.LightGray) },
            { "Arsenal", ("Arsenal", Color.LightGray) },
            { "Gallery", ("Gallery", Color.LightGray) },
            { "Bronzeworks", ("Bronzeworks", Color.LightGray) },
            { "Prosthetic Research", ("Prosthetic Research", Color.LightGray) },
            { "Synthflesh Sanctum", ("Synthflesh Sanctum", Color.LightGray) },
            { "Crucible of Transcendence", ("Crucible of Transcendence", Color.LightGray) },
            { "Viper's Loyals", ("Viper's Loyals", Color.LightGray) },
            { "Elite Legion", ("Elite Legion", Color.LightGray) },
            { "Steelflesh Quarters", ("Steelflesh Quarters", Color.LightGray) },
            { "Collective Legion", ("Collective Legion", Color.LightGray) },
            { "Foyer", ("Foyer", Color.LightGray) },
            { "Sealed Vault", ("Sealed Vault", Color.LightGray) },
        };

        private static readonly Dictionary<string, string> _roomTypes = new()
        {
            { "Guardhouse", "Garrison" }, { "Barracks", "Garrison" }, { "Hall of War", "Garrison" },
            { "Commander's Chamber", "Commander" }, { "Commander's Hall", "Commander" }, { "Commander's Headquarters", "Commander" },
            { "Depot", "Armoury" }, { "Arsenal", "Armoury" }, { "Gallery", "Armoury" },
            { "Bronzeworks", "Smithy" }, { "Chamber of Iron", "Smithy" }, { "Golden Forge", "Smithy" },
            { "Dynamo", "Generator" }, { "Shrine of Empowerment", "Generator" }, { "Solar Nexus", "Generator" },
            { "Spymaster's Study", "Spymaster" }, { "Hall of Shadows", "Spymaster" }, { "Omnipresent Panopticon", "Spymaster" },
            { "Viper's Loyals", "Legion Barracks" }, { "Elite Legion", "Legion Barracks" },
            { "Prosthetic Research", "Synthflesh Lab" }, { "Synthflesh Sanctum", "Synthflesh Lab" }, { "Crucible of Transcendence", "Synthflesh Lab" },
            { "Surgeon's Ward", "Flesh Surgeon" }, { "Surgeon's Theatre", "Flesh Surgeon" }, { "Surgeon's Symphony", "Flesh Surgeon" },
            { "Steelflesh Quarters", "Transcendent Barracks" }, { "Collective Legion", "Transcendent Barracks" },
            { "Chamber of Souls", "Alchemy Lab" }, { "Core Machinarium", "Alchemy Lab" }, { "Grand Phylactory", "Alchemy Lab" },
            { "Thaumaturge's Laboratory", "Thaumaturge" }, { "Thaumaturge's Cuttery", "Thaumaturge" }, { "Thaumaturge's Cathedral", "Thaumaturge" },
            { "Workshop", "Golem Works" }, { "Automaton Lab", "Golem Works" }, { "Stone Legion", "Golem Works" },
            { "Crimson Hall", "Corruption Chamber" }, { "Catalyst of Corruption", "Corruption Chamber" }, { "Locus of Corruption", "Corruption Chamber" },
            { "Sealed Vault", "Treasure Vault" },
            { "Altar of Sacrifice", "Sacrificial Chamber" }, { "Hall of Offerings", "Sacrificial Chamber" }, { "Apex of Oblation", "Sacrificial Chamber" },
            { "Architect's Chamber", "Architect's Chamber" },
            { "Foyer", "Entrance" },
            { "Kishara's Vault", "Currency Vault" },
            { "Vault of Reverence", "Lineage Gems Vault" },
            { "Jiquani's Vault", "Augments Vault" },
            { "Tablet Research Vault", "Tablets Vault" },
            { "Ancient Reliquary Vault", "Uniques Vault" },
            { "Royal Access Chamber", "Royal Access Chamber" },
            { "Extraction Chamber", "Extraction Chamber" },
            { "Atziri's Chamber", "Atziri's Chamber" },
            { "Sacrifice Room", "Sacrifice Room" },
            { "Path", "Path" }
        };

        private static readonly Dictionary<string, List<string>> _upgradedBy = new()
        {
            { "Commander", new List<string> { "Guardhouse", "Barracks", "Hall of War" } },
            { "Armoury", new List<string> { "Guardhouse", "Barracks", "Hall of War", "Viper's Loyals", "Elite Legion" } },
            { "Garrison", new List<string> { "Commander's Chamber", "Commander's Hall", "Commander's Headquarters" } },
            { "Transcendent Barracks", new List<string> { "Commander's Chamber", "Commander's Hall", "Commander's Headquarters" } },
            { "Smithy", new List<string> { "Depot", "Arsenal", "Gallery" } },
            { "Alchemy Lab", new List<string> { "Depot", "Arsenal", "Gallery" } },
            { "Golem Works", new List<string> { "Bronzeworks", "Chamber of Iron", "Golden Forge" } },
            { "Thaumaturge", new List<string> { "Dynamo", "Shrine of Empowerment", "Solar Nexus", "Chamber of Souls", "Core Machinarium", "Grand Phylactory", "Crimson Hall", "Catalyst of Corruption", "Locus of Corruption" } },
            { "Sacrificial Chamber", new List<string> { "Dynamo", "Shrine of Empowerment", "Solar Nexus", "Thaumaturge's Laboratory", "Thaumaturge's Cuttery", "Thaumaturge's Cathedral", "Crimson Hall", "Catalyst of Corruption", "Locus of Corruption" } },
            { "Spymaster", new List<string> { "Viper's Loyals", "Elite Legion" } },
            { "Flesh Surgeon", new List<string> { "Prosthetic Research", "Synthflesh Sanctum", "Crucible of Transcendence" } },
            { "Synthflesh Lab", new List<string> { "Surgeon's Ward", "Surgeon's Theatre", "Surgeon's Symphony", "Steelflesh Quarters", "Collective Legion" } },
            { "Sacrifice Room", new List<string> { "Altar of Sacrifice" } }
        };
        #endregion

        #region Core Logic
        public override bool Initialise()
        {
            foreach (var entity in GameController.EntityListWrapper.Entities)
            {
                EntityAdded(entity);
            }
            return true;
        }

        public override void AreaChange(AreaInstance area)
        {
            _pedestals.Clear();
            _rewards.Clear();
            _altarEntity = null;
            _cachedTileOverlays.Clear();
        }

        public override void EntityAdded(Entity entity)
        {
            var meta = entity.Metadata;
            if (string.IsNullOrEmpty(meta)) return;

            if (meta.StartsWith(PEDESTAL_METADATA))
            {
                var parts = meta.Split('_');
                if (parts.Length > 0 && int.TryParse(parts.Last(), out var number))
                {
                    _pedestals.Add(new PedestalInfo { Entity = entity, Number = number });
                }
            }
            else if (meta.Contains(ENCOUNTER_METADATA))
            {
                _altarEntity = entity;
            }
            else if (meta.StartsWith(BENCH_PREFIX))
            {
                var suffix = meta.Replace(BENCH_PREFIX, "");
                if (_rewardMapping.TryGetValue(suffix, out var info))
                {
                    _rewards.Add(new TrackedReward { Entity = entity, Info = info });
                }
            }
        }

        public override void EntityRemoved(Entity entity)
        {
            _pedestals.RemoveAll(p => p.Entity == entity);
            _rewards.RemoveAll(r => r.Entity == entity);
            if (_altarEntity == entity) _altarEntity = null;
        }
        #endregion

        #region Rendering - World
        public override void Render()
        {
            if (!Settings.Enable.Value || !GameController.InGame) return;

            var ui = GameController.IngameState.IngameUi;

            if (ui.TempleConsolePanel.IsVisible)
            {
                RenderTempleConsole(ui.TempleConsolePanel);
                return;
            }

            if (ui.PartyElement.IsVisible ||
                ui.SettingsPanel.IsVisible ||
                ui.SocialPanel.IsVisible ||
                ui.AtlasTreePanel.IsVisible ||
                ui.CraftBench.IsVisible ||
                ui.GemcuttingWindow.IsVisible ||
                ui.HelpWindow.IsVisible ||
                ui.InstanceManagerPanel.IsVisible ||
                ui.InventoryPanel.IsVisible ||
                ui.TreePanel.IsVisible ||
                ui.TempleOfferingWindow.IsVisible)
            {
                return;
            }

            var camera = GameController.IngameState.Camera;
            foreach (var p in _pedestals) p.Update(camera);
            
            var activatedSequence = _pedestals
                .Where(p => p.State == PedestalState.Activated)
                .OrderBy(p => p.ActivatedSeq)
                .ToList();

            if (Settings.ShowCircles) RenderCircles();
            if (Settings.ShowConnections) RenderConnections(activatedSequence);
            if (Settings.ShowNumbers) RenderNumbers(activatedSequence);
            
            RenderMainIncursionText(activatedSequence.Count);
            
            if (Settings.ShowRewards) 
            {
                RenderRewards();
                RenderGroundItemMedallions();
            }
        }

        private void RenderRewards()
        {
            var camera = GameController.IngameState.Camera;
            using (Graphics.SetTextScale(Settings.RewardTextScale))
            {
                foreach (var reward in _rewards)
                {
                    if (reward.Entity == null || !reward.Entity.IsValid || !reward.Entity.IsTargetable) continue;
                    var pos = camera.WorldToScreen(reward.Entity.Pos);
                    if (pos == Vector2.Zero) continue;

                    var titleSize = Graphics.MeasureText(reward.Info.Title);
                    var titlePos = new Vector2(pos.X - titleSize.X / 2, pos.Y - 40);
                    Graphics.DrawBox(new RectangleF(titlePos.X - 2, titlePos.Y - 2, titleSize.X + 4, titleSize.Y + 4), Color.FromArgb(160, 0, 0, 0));
                    Graphics.DrawText(reward.Info.Title, titlePos, Settings.RewardTitleColor);

                    var descSize = Graphics.MeasureText(reward.Info.Description);
                    var descPos = new Vector2(pos.X - descSize.X / 2, titlePos.Y + titleSize.Y + 2);
                    Graphics.DrawBox(new RectangleF(descPos.X - 2, descPos.Y - 2, descSize.X + 4, descSize.Y + 4), Color.FromArgb(160, 0, 0, 0));
                    Graphics.DrawText(reward.Info.Description, descPos, Settings.RewardDescColor);
                }
            }
        }

        private void RenderGroundItemMedallions()
        {
            var labels = GameController.IngameState.IngameUi.ItemsOnGroundLabels;
            if (labels == null) return;

            foreach (var labelEntity in labels)
            {
                if (labelEntity == null || !labelEntity.IsVisible) continue;
                
                var label = labelEntity.Label;
                if (label == null || !label.IsVisible) continue;

                var foundTextElement = FindTextElement(label, "Medallion");

                if (foundTextElement != null)
                {
                    var rect = label.GetClientRect();
                    Graphics.DrawFrame(rect, Color.Aqua, 3);
                }
            }
        }

        private void RenderMainIncursionText(int activatedCount)
        {
            if (_altarEntity == null) return;
            var pos = GameController.IngameState.Camera.WorldToScreen(_altarEntity.Pos);
            if (pos == Vector2.Zero) return;

            var text = $"Incursion: {activatedCount}/{_pedestals.Count}";
            var size = Graphics.MeasureText(text);
            var drawPos = pos - size / 2;
            
            Graphics.DrawBox(new RectangleF(drawPos.X - 8, drawPos.Y - 8, size.X + 16, size.Y + 16), Color.FromArgb(180, 0, 0, 0));
            Graphics.DrawText(text, drawPos, Color.White);
        }

        private void RenderCircles()
        {
            foreach (var p in _pedestals)
            {
                if (p.ScreenPosition == Vector2.Zero) continue;
                var color = p.State switch
                {
                    PedestalState.Activated => Settings.ActivatedColor.Value,
                    PedestalState.Queued => Settings.QueuedColor.Value,
                    _ => Settings.NotActivatedColor.Value
                };
                DrawSmoothCircle(p.ScreenPosition, Settings.CircleRadius, color, Settings.CircleThickness);
            }
        }

        private void RenderConnections(List<PedestalInfo> activatedSequence)
        {
            var r = Settings.CircleRadius.Value;
            var t = Settings.CircleThickness.Value;

            foreach (var p in _pedestals)
            {
                if (p.QueuePosition > 0 && p.ScreenPosition != Vector2.Zero)
                {
                    var prev = _pedestals.FirstOrDefault(x => x.ActivatedSeq == p.QueuePosition);
                    if (prev != null && prev.ScreenPosition != Vector2.Zero)
                        DrawLineEdgeToEdge(prev.ScreenPosition, p.ScreenPosition, r, t, Settings.ActivatedColor);
                }
            }

            var tip = _pedestals
                .Where(x => x.State != PedestalState.NotActivated)
                .OrderByDescending(x => x.QueuePosition)
                .ThenByDescending(x => x.ActivatedSeq)
                .FirstOrDefault();

            var unactivated = _pedestals
                .Where(x => x.State == PedestalState.NotActivated)
                .OrderBy(x => x.Number);

            var current = tip;
            foreach (var next in unactivated)
            {
                if (current != null && current.ScreenPosition != Vector2.Zero && next.ScreenPosition != Vector2.Zero)
                {
                    DrawLineEdgeToEdge(current.ScreenPosition, next.ScreenPosition, r, t, Settings.NotActivatedColor);
                }
                current = next;
            }
        }

        private void RenderNumbers(List<PedestalInfo> activatedSequence)
        {
            var lastSeq = activatedSequence.LastOrDefault()?.ActivatedSeq ?? 0;
            var nextUp = _pedestals.FirstOrDefault(p => p.State == PedestalState.Queued && p.QueuePosition == lastSeq + 1);

            using (Graphics.SetTextScale(Settings.NumberScale))
            {
                foreach (var p in _pedestals)
                {
                    if (p.ScreenPosition == Vector2.Zero) continue;

                    var isNext = p == nextUp;
                    var text = p.State switch
                    {
                        PedestalState.Activated => p.ActivatedSeq.ToString(),
                        PedestalState.Queued => (p.Number == 4) ? "NEXT" : $"Q{p.QueuePosition}",
                        _ => p.Number.ToString()
                    };

                    var size = Graphics.MeasureText(text);
                    var pos = new Vector2(p.ScreenPosition.X - size.X / 2, p.ScreenPosition.Y - (Settings.CircleRadius + size.Y + 5));
                    
                    var bgRect = new RectangleF(pos.X - 3, pos.Y - 3, size.X + 6, size.Y + 6);
                    Graphics.DrawBox(bgRect, isNext ? Color.FromArgb(150, 0, 100, 50) : Color.FromArgb(100, 0, 0, 0));
                    Graphics.DrawText(text, pos, isNext ? Settings.NextUpColor.Value : Settings.NumberColor.Value);
                }
            }
        }
        #endregion

        #region Rendering - UI Panels
        private void RenderTempleConsole(Element panel)
        {
            if (panel == null || panel.Children == null || panel.Children.Count <= 5) return;

            var tilesContainer = panel.Children[5];
            if (tilesContainer == null || tilesContainer.Children == null) return;

            if (DateTime.UtcNow - _lastTempleUpdate > _templeUpdateInterval)
            {
                _lastTempleUpdate = DateTime.UtcNow;
                UpdateTempleCache(tilesContainer);
            }

            var (typeColors, targetColors) = AssignUpgradeColors(panel);
            var tilePositions = new Dictionary<string, List<Vector2>>();

            foreach (var tile in tilesContainer.Children)
            {
                if (!tile.IsVisible) continue;

                if (_cachedTileOverlays.TryGetValue(tile.Address, out var info))
                {
                    Color? upgradeColor = null;
                    if (targetColors.TryGetValue(info.Id, out var c))
                    {
                        upgradeColor = c;
                    }
                    
                    info.IsUpgradeTarget = upgradeColor.HasValue;
                    
                    var tileRect = DrawTileOverlay(tile, info, upgradeColor);

                    if (info.IsUpgradeTarget && tileRect != RectangleF.Empty)
                    {
                        if (!tilePositions.ContainsKey(info.Id))
                        {
                            tilePositions[info.Id] = new List<Vector2>();
                        }
                        tilePositions[info.Id].Add(new Vector2(tileRect.X, tileRect.Center.Y));
                    }
                }
            }

            RenderRoomCards(panel, typeColors, tilePositions);
        }

        private (Dictionary<string, Color> TypeColors, Dictionary<string, Color> TargetColors) AssignUpgradeColors(Element panel)
        {
            var typeColors = new Dictionary<string, Color>();
            var targetColors = new Dictionary<string, Color>();
            
            if (panel.Children.Count <= 9) return (typeColors, targetColors);
            var containerL1 = panel.Children[9];
            if (containerL1 == null || containerL1.Children.Count <= 2) return (typeColors, targetColors);
            var cardsContainer = containerL1.Children[2];
            if (cardsContainer == null || cardsContainer.Children == null) return (typeColors, targetColors);

            int paletteIndex = 0;
            var usePalette = Settings.UseMultiColorUpgrades.Value;

            foreach (var card in cardsContainer.Children)
            {
                if (!card.IsVisible || card.Children.Count <= 3) continue;
                var textElement = card.Children[3];
                if (textElement == null || string.IsNullOrEmpty(textElement.Text)) continue;

                var roomName = textElement.Text.Trim();
                if (_roomTypes.TryGetValue(roomName, out var type))
                {
                    if (!typeColors.ContainsKey(type))
                    {
                        typeColors[type] = usePalette 
                            ? _upgradePalette[paletteIndex % _upgradePalette.Length] 
                            : Color.LightGreen;
                        paletteIndex++;
                    }

                    var color = typeColors[type];

                    if (_upgradedBy.TryGetValue(type, out var potentialTargets))
                    {
                        foreach (var target in potentialTargets)
                        {
                             if (_cachedTileOverlays.Values.Any(ov => ov.Id == target))
                             {
                                 if (!targetColors.ContainsKey(target))
                                 {
                                     targetColors[target] = color;
                                 }
                             }
                        }
                    }
                }
            }
            return (typeColors, targetColors);
        }

        private void RenderRoomCards(Element panel, Dictionary<string, Color> typeColors, Dictionary<string, List<Vector2>> tilePositions)
        {
            if (panel.Children.Count <= 9) return;
            var containerL1 = panel.Children[9];

            if (containerL1 == null || containerL1.Children.Count <= 2) return;
            var cardsContainer = containerL1.Children[2];

            if (cardsContainer == null || cardsContainer.Children == null) return;

            foreach (var card in cardsContainer.Children)
            {
                if (!card.IsVisible || card.Children.Count <= 3) continue;

                var textElement = card.Children[3];
                if (textElement == null || string.IsNullOrEmpty(textElement.Text)) continue;

                var roomName = textElement.Text.Trim();
                var cardRect = card.GetClientRect();

                var linesToDraw = new List<(string Text, Color Color, string TargetId)>();

                if (_roomNameMapping.TryGetValue(roomName, out var reward))
                {
                    linesToDraw.Add((reward.Text, reward.Color, null));
                }

                if (_roomTypes.TryGetValue(roomName, out var type) && 
                    typeColors.TryGetValue(type, out var typeColor) && 
                    _upgradedBy.TryGetValue(type, out var targets))
                {
                    var presentUpgrades = targets.Where(t => _cachedTileOverlays.Values.Any(ov => ov.Id == t)).ToList();
                    foreach (var target in presentUpgrades)
                    {
                        linesToDraw.Add(($"Upgrades: {target}", typeColor, target));
                    }
                }

                if (linesToDraw.Count == 0) continue;

                float totalHeight = 0;
                foreach (var line in linesToDraw)
                {
                    totalHeight += Graphics.MeasureText(line.Text).Y + 2;
                }
                totalHeight -= 2;

                var startY = cardRect.Center.Y - (totalHeight / 2);
                var startX = cardRect.Right + 10;

                foreach (var line in linesToDraw)
                {
                    var textSize = Graphics.MeasureText(line.Text);
                    var drawPos = new Vector2(startX, startY);
                    var bgRect = new RectangleF(drawPos.X - 2, drawPos.Y - 2, textSize.X + 4, textSize.Y + 4);
                    
                    Graphics.DrawBox(bgRect, Color.Black);
                    Graphics.DrawText(line.Text, drawPos, line.Color);
                    
                    if (Settings.ShowUpgradeLines && line.TargetId != null && tilePositions.TryGetValue(line.TargetId, out var positions))
                    {
                        var startPoint = new Vector2(bgRect.Right, bgRect.Center.Y);
                        foreach (var endPoint in positions)
                        {
                            Graphics.DrawLine(startPoint, endPoint, 2, Color.FromArgb(150, line.Color));
                        }
                    }

                    startY += textSize.Y + 2;
                }
            }
        }

        private void UpdateTempleCache(Element tilesContainer)
        {
            _cachedTileOverlays.Clear();
            foreach (var tile in tilesContainer.Children)
            {
                if (!tile.IsVisible) continue;
                
                var tooltip = tile.Tooltip;
                if (tooltip != null)
                {
                    var info = AnalyzeTileTooltip(tooltip);
                    if (info != null)
                    {
                        _cachedTileOverlays[tile.Address] = info;
                    }
                }
            }
        }

        private RectangleF DrawTileOverlay(Element tile, TileOverlayInfo info, Color? upgradeColor)
        {
            var isGeneric = info.Color == Color.LightGray;

            if (isGeneric && !info.IsUpgradeTarget) return RectangleF.Empty;

            var displayColor = upgradeColor ?? info.Color;
            
            var displayText = info.Text;
            if (info.Destabilizes) displayText += " (1-Use)";
            
            var rect = tile.GetClientRect();
            var center = rect.Center;
            var textSize = Graphics.MeasureText(displayText);
            var drawPos = new Vector2(center.X - textSize.X / 2, rect.Bottom - 15);
            
            var bgRect = new RectangleF(drawPos.X - 2, drawPos.Y - 2, textSize.X + 4, textSize.Y + 4);
            Graphics.DrawBox(bgRect, Color.Black);
            Graphics.DrawText(displayText, drawPos, displayColor);

            return bgRect;
        }
        #endregion

        #region Analysis & Helpers
        private string CollectAllText(Element element, int depth = 0)
        {
            if (element == null || depth > 20) return "";
            
            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(element.Text))
            {
                builder.AppendLine(element.Text);
            }

            if (element.Children != null)
            {
                foreach (var child in element.Children)
                {
                    builder.Append(CollectAllText(child, depth + 1));
                }
            }
            return builder.ToString();
        }

        private TileOverlayInfo AnalyzeTileTooltip(Element tooltip)
        {
            if (tooltip == null) return null;

            var fullText = CollectAllText(tooltip);
            if (string.IsNullOrEmpty(fullText)) return null;

            if (fullText.Contains("Contains Unique Item:"))
            {
                var match = Regex.Match(fullText, @"<unique>\{(.*?)\}");
                if (match.Success)
                {
                    var destab = fullText.Contains("Destabilises") || fullText.Contains("IncursionDestabilization");
                    return new TileOverlayInfo { Id = "Unique", Text = match.Groups[1].Value, Color = Color.Orange, Destabilizes = destab };
                }
            }

            foreach (var kvp in _roomNameMapping)
            {
                if (fullText.Contains(kvp.Key))
                {
                    var destab = fullText.Contains("Destabilises") || fullText.Contains("IncursionDestabilization");
                    return new TileOverlayInfo { Id = kvp.Key, Text = kvp.Value.Text, Color = kvp.Value.Color, Destabilizes = destab };
                }
            }

            return null;
        }

        private Element FindTextElement(Element element, string searchText, int depth = 0)
        {
            if (element == null || depth > 10) return null;

            if (!string.IsNullOrEmpty(element.Text) && element.Text.Contains(searchText))
            {
                return element;
            }

            if (element.Children != null)
            {
                foreach (var child in element.Children)
                {
                    var result = FindTextElement(child, searchText, depth + 1);
                    if (result != null) return result;
                }
            }
            return null;
        }

        private void DrawLineEdgeToEdge(Vector2 p1, Vector2 p2, float radius, int thickness, Color color)
        {
            var dir = p2 - p1;
            if (dir.Length() <= radius * 2) return;
            var norm = Vector2.Normalize(dir);
            Graphics.DrawLine(p1 + norm * radius, p2 - norm * radius, thickness, color);
        }

        private void DrawSmoothCircle(Vector2 center, float radius, Color color, int thickness)
        {
            var segments = Settings.CircleSegments.Value;
            if (segments < 3) return;
            var step = 2 * Math.PI / segments;
            var prev = center + new Vector2(radius, 0);

            for (var i = 1; i <= segments; i++)
            {
                var angle = i * step;
                var next = center + new Vector2((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
                Graphics.DrawLine(prev, next, thickness, color);
                prev = next;
            }
        }
        #endregion
    }
}