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

        private record RoomDef(string Name, string Type, string Overlay, Color Color, bool IsMaxTier = false);

        private static readonly List<RoomDef> _roomDefinitions = new()
        {
            new("Crimson Hall", "Corruption Chamber", "Corrupt", Color.Red),
            new("Catalyst of Corruption", "Corruption Chamber", "Corrupt", Color.Red),
            new("Locus of Corruption", "Corruption Chamber", "Corrupt", Color.Red, true),
            
            new("Thaumaturge's Laboratory", "Thaumaturge", "3-Link Gem", Color.Red),
            new("Thaumaturge's Cuttery", "Thaumaturge", "Gem Quality", Color.Red),
            new("Thaumaturge's Cathedral", "Thaumaturge", "Gem Corrupt", Color.Red, true),

            new("Chamber of Souls", "Alchemy Lab", "Rarity + Medallion", Color.Red),
            new("Core Machinarium", "Alchemy Lab", "Rarity + Medallion", Color.Red),
            new("Grand Phylactory", "Alchemy Lab", "Corrupt Soul Core", Color.Red, true),

            new("Tablet Research Vault", "Tablets Vault", "Tablet Corrupt", Color.Red),
            
            new("Altar of Sacrifice", "Sacrificial Chamber", "Unique Item", Color.Orange),
            new("Hall of Offerings", "Sacrificial Chamber", "Unique Item", Color.Orange),
            new("Apex of Oblation", "Sacrificial Chamber", "Unique Item", Color.Orange, true),
            new("Ancient Reliquary Vault", "Uniques Vault", "Unique Item", Color.Orange),
            
            new("Kishara's Vault", "Currency Vault", "Currency", Color.Gold),
            new("Jiquani's Vault", "Augments Vault", "High Lvl Rune", Color.Cyan),
            new("Vault of Reverence", "Lineage Gems Vault", "Lineage Gem", Color.Cyan),
            
            new("Commander's Chamber", "Commander", "Uromoti", Color.Wheat),
            new("Commander's Hall", "Commander", "Uromoti", Color.Wheat),
            new("Commander's Headquarters", "Commander", "Uromoti", Color.Wheat, true),
            
            new("Spymaster's Study", "Spymaster", "Juatalotli", Color.Wheat),
            new("Hall of Shadows", "Spymaster", "Juatalotli", Color.Wheat),
            new("Omnipresent Panopticon", "Spymaster", "Juatalotli", Color.Wheat, true),
            
            new("Workshop", "Golem Works", "Quipolatl", Color.Wheat),
            new("Automaton Lab", "Golem Works", "Quipolatl", Color.Wheat),
            new("Stone Legion", "Golem Works", "Quipolatl", Color.Wheat, true),
            
            new("Architect's Chamber", "Architect's Chamber", "Xopec/Azcapa", Color.Wheat),

            new("Bronzeworks", "Smithy", "Quality Bench", Color.LightGray),
            new("Chamber of Iron", "Smithy", "Socket Bench", Color.LightGray),
            new("Golden Forge", "Smithy", "Quality >20%", Color.LightGray, true),
            
            new("Dynamo", "Generator", "Power/Bench", Color.LightGray),
            new("Shrine of Empowerment", "Generator", "Power/Bench", Color.LightGray),
            new("Solar Nexus", "Generator", "Power/Bench", Color.LightGray, true),
            
            new("Surgeon's Ward", "Flesh Surgeon", "Limb Mod", Color.LightGray),
            new("Surgeon's Theatre", "Flesh Surgeon", "Limb Mod", Color.LightGray),
            new("Surgeon's Symphony", "Flesh Surgeon", "Limb Mod", Color.LightGray, true),
            
            new("Extraction Chamber", "Extraction Chamber", "Extract Augments", Color.LightGray),
            
            new("Royal Access Chamber", "Royal Access Chamber", "Access Atziri", Color.Magenta),
            new("Atziri's Chamber", "Atziri's Chamber", "Atziri", Color.Magenta),
            new("Sacrifice Room", "Sacrifice Room", "Sacrifice Room", Color.Magenta),

            new("Path", "Path", "", Color.LightGray),
            new("Guardhouse", "Garrison", "Equip Mod", Color.LightGray),
            new("Barracks", "Garrison", "Equip Mod", Color.LightGray),
            new("Hall of War", "Garrison", "Equip Mod", Color.LightGray, true),
            
            new("Depot", "Armoury", "Equipment", Color.LightGray),
            new("Arsenal", "Armoury", "Equipment", Color.LightGray),
            new("Gallery", "Armoury", "Equipment", Color.LightGray, true),
            
            new("Prosthetic Research", "Synthflesh Lab", "Experience", Color.LightGray),
            new("Synthflesh Sanctum", "Synthflesh Lab", "Experience", Color.LightGray),
            new("Crucible of Transcendence", "Synthflesh Lab", "Experience", Color.LightGray, true),
            
            new("Viper's Loyals", "Legion Barracks", "Rare Monsters", Color.LightGray),
            new("Elite Legion", "Legion Barracks", "Rare Monsters", Color.LightGray, true),
            
            new("Steelflesh Quarters", "Transcendent Barracks", "Magic Monsters", Color.LightGray),
            new("Collective Legion", "Transcendent Barracks", "Magic Monsters", Color.LightGray, true),
            
            new("Foyer", "Entrance", "", Color.LightGray),
            new("Sealed Vault", "Treasure Vault", "Chests", Color.LightGray),
        };

        private static readonly Dictionary<string, (string Text, Color Color)> _roomNameMapping = 
            _roomDefinitions.ToDictionary(r => r.Name, r => (r.Overlay, r.Color));

        private static readonly Dictionary<string, string> _roomTypes = 
            _roomDefinitions.ToDictionary(r => r.Name, r => r.Type);

        private static readonly HashSet<string> _maxTierRooms = 
            _roomDefinitions.Where(r => r.IsMaxTier).Select(r => r.Name).ToHashSet();

        private static readonly Dictionary<string, Color> _medallionColors = new()
        {
            { "Juatalotli", Color.LightGreen },
            { "Hayoxi", Color.Yellow },
            { "Quipolatl", Color.Magenta },
            { "Uromoti", Color.Orange },
            { "Xopec", Color.Blue },
            { "Azcapa", Color.Cyan },
            { "Estazunti", Color.Gold }
        };

        private static readonly Dictionary<string, string> _medallionDescriptions = new()
        {
            { "Juatalotli", "Prevent next Destabilisation" },
            { "Hayoxi", "Reroll Reward Vault reward" },
            { "Quipolatl", "Improve Room Tier" },
            { "Uromoti", "Add Room" },
            { "Xopec", "Increase Max Crystal Capacity" },
            { "Azcapa", "Increase Max Medallion Capacity" },
            { "Estazunti", "Extra Reward Vault" }
        };

        private static readonly Dictionary<string, List<string>> _upgradedBy = new()
        {
            { "Commander", new List<string> { "Guardhouse", "Barracks", "Hall of War" } },
            { "Armoury", new List<string> { "Guardhouse", "Barracks", "Hall of War", "Viper's Loyals", "Elite Legion" } },
            { "Garrison", new List<string> { "Commander's Chamber", "Commander's Hall", "Commander's Headquarters" } },
            { "Transcendent Barracks", new List<string> { "Commander's Chamber", "Commander's Hall", "Commander's Headquarters" } },
            { "Smithy", new List<string> { "Depot", "Arsenal", "Gallery" } },
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

            var activatedCount = activatedSequence.Count;
            var totalCount = _pedestals.Count;
            var isComplete = totalCount > 0 && activatedCount == totalCount;

            if (!isComplete)
            {
                if (Settings.ShowCircles) RenderCircles();
                if (Settings.ShowConnections) RenderConnections(activatedSequence);
                if (Settings.ShowNumbers) RenderNumbers(activatedSequence);
            }
            
            RenderMainIncursionText(activatedCount, totalCount, isComplete);
            
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

                var fullText = CollectAllText(label);
                if (string.IsNullOrEmpty(fullText)) continue;

                if (fullText.Contains("Medallion"))
                {
                    var color = Color.Aqua; // Default
                    foreach (var kvp in _medallionColors)
                    {
                        if (fullText.Contains(kvp.Key))
                        {
                            color = kvp.Value;
                            break;
                        }
                    }

                    var rect = label.GetClientRect();
                    Graphics.DrawFrame(rect, color, 3);
                }
            }
        }

        private void RenderMainIncursionText(int activatedCount, int totalCount, bool isComplete)
        {
            if (_altarEntity == null) return;
            var pos = GameController.IngameState.Camera.WorldToScreen(_altarEntity.Pos);
            if (pos == Vector2.Zero) return;

            string text;
            Color color;

            if (isComplete)
            {
                text = "COMPLETE";
                color = Color.LimeGreen;
            }
            else
            {
                text = $"Incursion: {activatedCount}/{totalCount}";
                color = Color.White;
            }

            var size = Graphics.MeasureText(text);
            var drawPos = pos - size / 2;
            
            Graphics.DrawBox(new RectangleF(drawPos.X - 8, drawPos.Y - 8, size.X + 16, size.Y + 16), Color.FromArgb(180, 0, 0, 0));
            Graphics.DrawText(text, drawPos, color);
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
            
            bool isAnyTileHovered = false;
            if (Settings.AutoHideOnHover.Value)
            {
                foreach (var tile in tilesContainer.Children)
                {
                    if (tile.IsVisible && tile.HasShinyHighlight && tile.Tooltip != null)
                    {
                        if (_cachedTileOverlays.TryGetValue(tile.Address, out var info))
                        {
                            var isGenericWithNoText = info.Color == Color.LightGray && string.IsNullOrEmpty(info.Text);
                            if (!isGenericWithNoText)
                            {
                                isAnyTileHovered = true;
                                break;
                            }
                        }
                    }
                }
            }

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
                    
                    var tileRect = DrawTileOverlay(tile, info, upgradeColor, isAnyTileHovered);

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

            RenderRoomCards(panel, typeColors, tilePositions, isAnyTileHovered);
            RenderMedallionCards(panel);
        }

        private void RenderMedallionCards(Element panel)
        {
            if (panel.Children.Count <= 11) return;
            var containerL1 = panel.Children[11];
            
            if (containerL1 == null || containerL1.Children.Count <= 2) return;
            var cardsContainer = containerL1.Children[2];
            
            if (cardsContainer == null || cardsContainer.Children == null) return;

            for (int i = 0; i < cardsContainer.Children.Count; i++)
            {
                var card = cardsContainer.Children[i];
                if (!card.IsVisible) continue;

                var allText = CollectAllText(card);
                
                if (card.Tooltip != null)
                {
                    allText += Environment.NewLine + CollectAllText(card.Tooltip);
                }

                if (string.IsNullOrEmpty(allText)) continue;

                var cardRect = card.GetClientRect();
                string description = null;
                Color color = Color.White;

                var match = Regex.Match(allText, @"Use to add Room:[\s\r\n]+([^\r\n]+)");
                if (match.Success)
                {
                    var roomName = match.Groups[1].Value.Trim();
                    var endLine = roomName.IndexOfAny(new[] { '\r', '\n' });
                    if (endLine > 0) roomName = roomName.Substring(0, endLine).Trim();

                    description = $"Adds: {roomName}";
                    
                    var roomDef = _roomDefinitions.FirstOrDefault(r => r.Name.Equals(roomName, StringComparison.OrdinalIgnoreCase));
                    if (roomDef == null)
                    {
                        roomDef = _roomDefinitions.FirstOrDefault(r => r.Type.Equals(roomName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (roomDef != null)
                    {
                        color = roomDef.Color;
                    }
                    else
                    {
                        color = Color.Orange;
                    }
                }

                if (description == null)
                {
                    foreach (var kvp in _medallionDescriptions)
                    {
                        if (allText.Contains(kvp.Key))
                        {
                            description = kvp.Value;
                            if (_medallionColors.TryGetValue(kvp.Key, out var c)) color = c;
                            break;
                        }
                    }
                }

                if (description != null)
                {
                    var textSize = Graphics.MeasureText(description);
                    var windowRect = GameController.Window.GetWindowRectangle();
                    
                    var drawPos = new Vector2(cardRect.Right + 10, cardRect.Center.Y - textSize.Y / 2);
                    
                    if (drawPos.X + textSize.X > windowRect.Width - 10)
                    {
                        drawPos.X = cardRect.Left - 10 - textSize.X;
                    }

                    var bgRect = new RectangleF(drawPos.X - 2, drawPos.Y - 2, textSize.X + 4, textSize.Y + 4);
                    
                    Graphics.DrawBox(bgRect, Color.Black);
                    Graphics.DrawText(description, drawPos, color);
                }
            }
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
                                 if (_maxTierRooms.Contains(target)) continue;

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

        private void RenderRoomCards(Element panel, Dictionary<string, Color> typeColors, Dictionary<string, List<Vector2>> tilePositions, bool isAnyTileHovered)
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
                    if (!string.IsNullOrEmpty(reward.Text))
                    {
                        linesToDraw.Add((reward.Text, reward.Color, null));
                    }
                }

                if (_roomTypes.TryGetValue(roomName, out var type) && 
                    typeColors.TryGetValue(type, out var typeColor) && 
                    _upgradedBy.TryGetValue(type, out var targets))
                {
                    var presentUpgrades = targets
                        .Where(t => _cachedTileOverlays.Values.Any(ov => ov.Id == t) && !_maxTierRooms.Contains(t))
                        .ToList();

                    foreach (var target in presentUpgrades)
                    {
                        linesToDraw.Add(($"Upgrades: {target}", typeColor, target));
                    }
                }

                var upgraderEntry = _upgradedBy.FirstOrDefault(x => x.Value.Contains(roomName));
                if (!string.IsNullOrEmpty(upgraderEntry.Key))
                {
                    var upgraderType = upgraderEntry.Key;
                    var isUpgraderPresent = _cachedTileOverlays.Values.Any(ov => 
                        _roomTypes.TryGetValue(ov.Id, out var existingType) && existingType == upgraderType);

                    if (isUpgraderPresent)
                    {
                        Color upgraderColor = Color.Yellow;
                        if (typeColors.TryGetValue(upgraderType, out var c)) upgraderColor = c;
                        else
                        {
                            var def = _roomDefinitions.FirstOrDefault(r => r.Type == upgraderType);
                            if (def != null) upgraderColor = def.Color;
                        }

                        linesToDraw.Add(($"Upgraded by: {upgraderType}", upgraderColor, upgraderType));
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
                    
                    if (Settings.ShowUpgradeLines && !isAnyTileHovered && line.TargetId != null)
                    {
                        if (tilePositions.TryGetValue(line.TargetId, out var positions))
                        {
                            var startPoint = new Vector2(bgRect.Right, bgRect.Center.Y);
                            foreach (var endPoint in positions)
                            {
                                Graphics.DrawLine(startPoint, endPoint, 2, Color.FromArgb(150, line.Color));
                            }
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

        private RectangleF DrawTileOverlay(Element tile, TileOverlayInfo info, Color? upgradeColor, bool isAnyTileHovered)
        {
            var isGeneric = info.Color == Color.LightGray;

            if (isGeneric && !info.IsUpgradeTarget && string.IsNullOrEmpty(info.Text)) return RectangleF.Empty;

            var displayColor = upgradeColor ?? info.Color;
            
            var displayText = info.Text;
            if (info.Destabilizes) displayText += " (1-Use)";
            
            var rect = tile.GetClientRect();
            var center = rect.Center;
            var textSize = Graphics.MeasureText(displayText);
            var drawPos = new Vector2(center.X - textSize.X / 2, rect.Bottom - 15);
            
            var bgRect = new RectangleF(drawPos.X - 2, drawPos.Y - 2, textSize.X + 4, textSize.Y + 4);
            
            if (!isAnyTileHovered)
            {
                Graphics.DrawBox(bgRect, Color.Black);
                Graphics.DrawText(displayText, drawPos, displayColor);
            }

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