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
        public string Text;
        public Color Color;
        public bool Destabilizes;
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
            { "Sacrifice Room", ("Upgrade Room", Color.Magenta) },
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

            foreach (var tile in tilesContainer.Children)
            {
                if (!tile.IsVisible) continue;

                if (_cachedTileOverlays.TryGetValue(tile.Address, out var info))
                {
                    DrawTileOverlay(tile, info);
                }
            }

            RenderRoomCards(panel);
        }

        private void RenderRoomCards(Element panel)
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
                if (_roomNameMapping.TryGetValue(roomName, out var reward))
                {
                    var cardRect = card.GetClientRect();
                    var textSize = Graphics.MeasureText(reward.Text);
                    var drawPos = new Vector2(cardRect.Right + 10, cardRect.Center.Y - textSize.Y / 2);

                    Graphics.DrawBox(new RectangleF(drawPos.X - 2, drawPos.Y - 2, textSize.X + 4, textSize.Y + 4), Color.Black);
                    Graphics.DrawText(reward.Text, drawPos, reward.Color);
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

        private void DrawTileOverlay(Element tile, TileOverlayInfo info)
        {
            var displayText = info.Text + (info.Destabilizes ? " (1-Use)" : "");
            var rect = tile.GetClientRect();
            var center = rect.Center;
            var textSize = Graphics.MeasureText(displayText);
            var drawPos = new Vector2(center.X - textSize.X / 2, rect.Bottom - 15);
            
            Graphics.DrawBox(new RectangleF(drawPos.X - 2, drawPos.Y - 2, textSize.X + 4, textSize.Y + 4), Color.Black);
            Graphics.DrawText(displayText, drawPos, info.Color);
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
                    return new TileOverlayInfo { Text = match.Groups[1].Value, Color = Color.Orange, Destabilizes = destab };
                }
            }

            foreach (var kvp in _roomNameMapping)
            {
                if (fullText.Contains(kvp.Key))
                {
                    var destab = fullText.Contains("Destabilises") || fullText.Contains("IncursionDestabilization");
                    return new TileOverlayInfo { Text = kvp.Value.Text, Color = kvp.Value.Color, Destabilizes = destab };
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