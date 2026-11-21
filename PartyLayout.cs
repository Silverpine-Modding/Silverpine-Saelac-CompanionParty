using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CompanionParty;

internal static class PartyLayout
{
    internal static readonly Color Surface = new(.105f, .13f, .16f, .99f);
    internal static readonly Color Control = new(.19f, .23f, .27f, 1);
    internal static readonly Color Accent = new(.19f, .37f, .32f, 1);
    internal static RectTransform Rect(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }
    internal static void Stretch(RectTransform r, float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
    }
    internal static Image Background(RectTransform r, Color color, bool raycast = true)
    {
        var image = r.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast;
        return image;
    }
    internal static void Size(RectTransform r, float height, float width = -1)
    {
        var e = r.gameObject.AddComponent<LayoutElement>(); e.minHeight = e.preferredHeight = height;
        if (width >= 0) e.minWidth = e.preferredWidth = width;
        else e.flexibleWidth = 1;
    }
    internal static TextMeshProUGUI Text(RectTransform r, string caption, float size = 17)
    {
        var label = r.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = caption; label.fontSize = size; label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
        label.richText = false; label.raycastTarget = false;
        return label;
    }
    internal static TextMeshProUGUI Line(Transform parent, string caption, float height = 28)
    {
        var r = Rect(caption, parent); Size(r, height); return Text(r, caption);
    }
    internal static Button Button(Transform parent, string caption, Action click, float width = -1, bool chosen = false)
    {
        var r = Rect(caption, parent); Size(r, 36, width);
        var bg = Background(r, chosen ? Accent : Control);
        var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = bg;
        button.onClick.AddListener(() => click());
        var label = Rect("Label", r); Stretch(label, 9, 0, 9, 0);
        Text(label, caption).alignment = TextAlignmentOptions.Center;
        return button;
    }
    internal static void Vertical(RectTransform r, int gap = 5)
    {
        var layout = r.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = gap; layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
    }
    internal static RectTransform Row(Transform parent, float height = 36)
    {
        var r = Rect("Row", parent); Size(r, height);
        var layout = r.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 6;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        return r;
    }
    internal static RectTransform Scroll(RectTransform viewport)
    {
        Background(viewport, Color.clear); viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        var content = Rect("Rows", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
        Vertical(content);
        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
        return content;
    }
    internal static void Remove(GameObject go)
    {
        if (!go) return;
        go.SetActive(false); UnityEngine.Object.Destroy(go);
    }
}
