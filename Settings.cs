using System;
using System.Drawing;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;
using ExileCore2.Shared.Attributes;

namespace IncursionHelper
{

public class Settings : ISettings
{
    [Menu("Enabled")]
    public ToggleNode Enable { get; set; } = new ToggleNode(true);
    
    [Menu("Show Numbers")]
    public ToggleNode ShowNumbers { get; set; } = new ToggleNode(true);
    
    [Menu("Show Circles")]
    public ToggleNode ShowCircles { get; set; } = new ToggleNode(true);
    
    [Menu("Show Connections")]
    public ToggleNode ShowConnections { get; set; } = new ToggleNode(true);

    [Menu("Show Rewards", 10)]
    public ToggleNode ShowRewards { get; set; } = new ToggleNode(true);

    [Menu("Reward Title Color", 10)]
    public ColorNode RewardTitleColor { get; set; } = new ColorNode(Color.Cyan);

    [Menu("Reward Desc Color", 10)]
    public ColorNode RewardDescColor { get; set; } = new ColorNode(Color.White);

    [Menu("Reward Text Scale", 10)]
    public RangeNode<float> RewardTextScale { get; set; } = new RangeNode<float>(1.0f, 0.5f, 3.0f);

    [Menu("Use Multi-Color Upgrades", "Use different colors for different upgrade chains")]
    public ToggleNode UseMultiColorUpgrades { get; set; } = new ToggleNode(true);

    [Menu("Show Upgrade Lines")]
    public ToggleNode ShowUpgradeLines { get; set; } = new ToggleNode(true);
    
    [Menu("Activated Color")]
    public ColorNode ActivatedColor { get; set; } = new ColorNode(Color.FromArgb(128, 0, 255, 0));
    
    [Menu("Queued Color")]
    public ColorNode QueuedColor { get; set; } = new ColorNode(Color.FromArgb(128, 255, 165, 0));
    
    [Menu("Next Up Color")]
    public ColorNode NextUpColor { get; set; } = new ColorNode(Color.FromArgb(200, 0, 255, 100));
    
    [Menu("Not Activated Color")]
    public ColorNode NotActivatedColor { get; set; } = new ColorNode(Color.FromArgb(128, 255, 0, 0));
    
    [Menu("Number Color")]
    public ColorNode NumberColor { get; set; } = new ColorNode(Color.White);
    
    [Menu("Circle Radius")]
    public RangeNode<float> CircleRadius { get; set; } = new RangeNode<float>(25f, 10f, 50f);
    
    [Menu("Circle Segments")]
    public RangeNode<int> CircleSegments { get; set; } = new RangeNode<int>(32, 8, 64);
    
    [Menu("Circle Thickness")]
    public RangeNode<int> CircleThickness { get; set; } = new RangeNode<int>(3, 1, 10);
    
    [Menu("Number Scale")]
    public RangeNode<float> NumberScale { get; set; } = new RangeNode<float>(1.0f, 0.5f, 2.0f);

    [Menu("Arc Multiplier", "Controls the curve of the connection lines. 0 for straight lines.")]
    public RangeNode<float> ArcMultiplier { get; set; } = new RangeNode<float>(0.2f, 0f, 1f);

    [Menu("Auto-hide on Hover", "Hide overlays when hovering a tile with a tooltip")]
    public ToggleNode AutoHideOnHover { get; set; } = new ToggleNode(true);
}
}