using Godot;

namespace Cataclysm3D.Game.UI;

/// <summary>
/// A small panel beside the crosshair naming what you face and the keys for the things you can
/// do to it, e.g. "[W] Open the door · [F] Smash the door". Ignores the mouse, like the rest of
/// the HUD, so it never swallows mouse-look.
/// </summary>
public partial class InteractionTooltip : PanelContainer
{
    private readonly Label _title = new();
    private readonly VBoxContainer _rows = new();

    public InteractionTooltip()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.06f, 0.72f),
            BorderColor = new Color(1, 1, 1, 0.12f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 8,
        });

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        _title.MouseFilter = MouseFilterEnum.Ignore;
        _title.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.7f));
        _title.AddThemeFontSizeOverride("font_size", 15);
        _rows.MouseFilter = MouseFilterEnum.Ignore;
        _rows.AddThemeConstantOverride("separation", 2);
        box.AddChild(_title);
        box.AddChild(_rows);
        AddChild(box);

        // Just right of and below the crosshair at the screen's center.
        SetAnchorsPreset(LayoutPreset.Center);
        GrowHorizontal = GrowDirection.End;
        GrowVertical = GrowDirection.End;
        Position = new Vector2(28, 20);
    }

    /// <summary>Shows a title and up to a few (key, action) rows; hides when there are none.</summary>
    public void Show(string title, IReadOnlyList<(string Key, string Text)> rows)
    {
        if (rows.Count == 0)
        {
            Visible = false;
            return;
        }
        _title.Text = title;
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var (key, text) in rows)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 8);
            var chip = new Label
            {
                Text = key,
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(22, 0),
            };
            chip.AddThemeColorOverride("font_color", Colors.Black);
            chip.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = new Color(0.92f, 0.9f, 0.84f),
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
                ContentMarginLeft = 5, ContentMarginRight = 5,
            });
            var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(chip);
            row.AddChild(label);
            _rows.AddChild(row);
        }
        // Shrink back to the content when the list gets shorter.
        Size = Vector2.Zero;
        Visible = true;
    }
}
