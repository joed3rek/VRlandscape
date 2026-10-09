using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VRLandscape
{
    // Park list in neo-brutalist style: a 40/60 split with a loud title zone on the left and park "sticker"
    // cards on the right. Screen-space on phones, laser-clickable panel on Quest.
    // Each park offers Walk in 3D / 360 Tour / AR depending on its content.
    public class MainMenu : MonoBehaviour
    {
        public const int PageSize = 4;
        const float Split = 0.4f;   // title zone | park list

        public static MainMenu Create(List<ParkDef> parks, bool world, int page,
            Action<ParkDef> onWalk, Action<ParkDef> onPanorama, Action<ParkDef> onAR, Action<ParkDef> onSite,
            Action onLoadFile, Action onSync, Action<ParkDef> onDelete, Action onReload, Action<int> onPage)
        {
            var canvas = UIFactory.CreateCanvas("Main Menu", world);
            var menu = canvas.gameObject.AddComponent<MainMenu>();
            var root = UIFactory.Panel(canvas.transform, Theme.Cream, "Background");
            UIFactory.Pattern(root, Theme.Grid, 40f);

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(parks.Count / (float)PageSize));
            page = Mathf.Clamp(page, 0, pageCount - 1);

            BuildParkColumn(root, parks, world, page, pageCount, onWalk, onPanorama, onAR, onSite, onLoadFile, onSync, onDelete, onReload, onPage);
            BuildTitleZone(root, world);
            return menu;
        }

        // ------------------------------------------------------------ right: parks

        static void BuildParkColumn(RectTransform root, List<ParkDef> parks, bool world, int page, int pageCount,
            Action<ParkDef> onWalk, Action<ParkDef> onPanorama, Action<ParkDef> onAR, Action<ParkDef> onSite,
            Action onLoadFile, Action onSync, Action<ParkDef> onDelete, Action onReload, Action<int> onPage)
        {
            var column = UIFactory.Rect(root, "Parks");
            column.anchorMin = new Vector2(Split, 0);
            column.anchorMax = new Vector2(1, 1);
            column.offsetMin = new Vector2(24, 36);
            column.offsetMax = new Vector2(-48, -40);

            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 26;
            layout.padding = new RectOffset(0, 16, 0, 12);   // room for hard shadows
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childAlignment = TextAnchor.UpperLeft;

            // "Select a park" label sticker
            var labelRow = UIFactory.Rect(column, "Label Row");
            UIFactory.Layout(labelRow, preferredHeight: 58);
            var label = UIFactory.Badge(labelRow, "Select a park", Theme.Ink, Theme.Small + 2, -1.5f, Theme.Yellow);
            UIFactory.Place(label, new Vector2(0, 0.5f), new Vector2(300, 52), new Vector2(4, 0));

            int first = page * PageSize;
            for (int i = first; i < Mathf.Min(parks.Count, first + PageSize); i++)
                ParkCard(column, parks[i], i, world, onWalk, onPanorama, onAR, onSite, onDelete);

            var spacer = UIFactory.Rect(column, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

            // Footer: paging, reload, load file
            var footer = UIFactory.Rect(column, "Footer");
            var fl = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            fl.spacing = 18;
            fl.childControlWidth = fl.childControlHeight = true;
            fl.childForceExpandWidth = false;
            fl.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.Layout(footer, preferredHeight: 76);

            var prev = UIFactory.Button(footer, "←", () => onPage(page - 1), world, Theme.White, 36);
            prev.interactable = page > 0;
            UIFactory.Layout(prev, preferredWidth: 90);
            var pageBadge = UIFactory.Rect(footer, "Page");
            UIFactory.Layout(pageBadge, preferredWidth: 130);
            var badge = UIFactory.Badge(pageBadge, $"{page + 1} / {pageCount}", Theme.Violet, Theme.Small, 2f);
            UIFactory.Place(badge, new Vector2(0.5f, 0.5f), new Vector2(120, 54), Vector2.zero);
            var next = UIFactory.Button(footer, "→", () => onPage(page + 1), world, Theme.White, 36);
            next.interactable = page < pageCount - 1;
            UIFactory.Layout(next, preferredWidth: 90);

            var reload = UIFactory.Button(footer, "Reload", onReload, world, Theme.White, 26);
            UIFactory.Layout(reload, preferredWidth: 160);
            if (onSync != null)
            {
                var sync = UIFactory.Button(footer, "Sync", onSync, world, Theme.Violet, 26);
                UIFactory.Layout(sync, preferredWidth: 140);
            }
            if (onLoadFile != null)
            {
                var load = UIFactory.Button(footer, "+ Load file", onLoadFile, world, Theme.Red, 26);
                UIFactory.Layout(load, preferredWidth: 240);
            }
        }

        static void ParkCard(Transform parent, ParkDef park, int index, bool world,
            Action<ParkDef> onWalk, Action<ParkDef> onPanorama, Action<ParkDef> onAR, Action<ParkDef> onSite,
            Action<ParkDef> onDelete)
        {
            var card = UIFactory.Card(parent, Theme.White, Theme.ShadowMd, "Park " + park.name);
            UIFactory.Layout(card, preferredHeight: 148);
            UIFactory.Tilt(card, index % 2 == 0 ? -0.5f : 0.5f);

            var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(18, 22, 16, 16);
            row.spacing = 18;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            row.childAlignment = TextAnchor.MiddleLeft;

            // Index block: "01" in a yellow box
            var number = UIFactory.Card(card, index % 3 == 1 ? Theme.Violet : Theme.Yellow, Vector2.zero, "Number");
            UIFactory.Layout(number, preferredWidth: 112);
            var n = UIFactory.Heading(number, (index + 1).ToString("00"), Theme.H2, TextAnchor.MiddleCenter);
            UIFactory.Stretch(n.rectTransform);

            // Name + details
            var info = UIFactory.Rect(card, "Info");
            UIFactory.Layout(info, flexibleWidth: 1);
            var name = UIFactory.Heading(info, UIFactory.Shorten(park.name, 44), Theme.H3, TextAnchor.LowerLeft);
            UIFactory.FitInside(name, 22);
            name.rectTransform.anchorMin = new Vector2(0, 0.45f);
            name.rectTransform.anchorMax = Vector2.one;
            name.rectTransform.offsetMin = name.rectTransform.offsetMax = Vector2.zero;

            string details = park.description;
            if (string.IsNullOrEmpty(details))
                details = (park.HasModel ? "3D model" : "") + (park.HasModel && park.HasPanoramas ? " + " : "") +
                          (park.HasPanoramas ? $"{park.panoramas.Length} panoramas" : "");
            var desc = UIFactory.Label(info, UIFactory.Shorten(details, 90), Theme.Small, TextAnchor.UpperLeft);
            UIFactory.FitInside(desc, 16);
            desc.rectTransform.anchorMin = Vector2.zero;
            desc.rectTransform.anchorMax = new Vector2(1, 0.42f);
            desc.rectTransform.offsetMin = desc.rectTransform.offsetMax = Vector2.zero;

            // Actions
            if (park.HasModel)
                UIFactory.Layout(UIFactory.Button(card, "Walk 3D", () => onWalk(park), world, Theme.Red, 26), preferredWidth: 170);
            if (park.HasPanoramas)
                UIFactory.Layout(UIFactory.Button(card, "360°", () => onPanorama(park), world, Theme.Violet, 28), preferredWidth: 130);
            if (park.HasModel && onAR != null)
                UIFactory.Layout(UIFactory.Button(card, "AR", () => onAR(park), world, Theme.Yellow, 28), preferredWidth: 100);
            if (park.HasPanoramas && onSite != null)
                UIFactory.Layout(UIFactory.Button(card, "On site", () => onSite(park), world, Theme.White, 22), preferredWidth: 130);

            // Imported parks can be deleted: the first tap arms the button, the second deletes.
            if (onDelete != null && ParkCatalog.IsDeletable(park))
            {
                Button delete = null;
                bool armed = false;
                delete = UIFactory.Button(card, "×", () =>
                {
                    if (armed) { onDelete(park); return; }
                    armed = true;
                    UIFactory.SetButtonLabel(delete, "Sure?");
                    delete.GetComponentInChildren<Text>().fontSize = 22;
                    delete.targetGraphic.color = Theme.Red;
                }, world, Theme.White, 40);
                UIFactory.Layout(delete, preferredWidth: 96);
            }
        }

        // ------------------------------------------------------------ left: title zone

        static void BuildTitleZone(RectTransform root, bool world)
        {
            var zone = UIFactory.Rect(root, "Title Zone");
            zone.anchorMin = new Vector2(0, 0);
            zone.anchorMax = new Vector2(Split, 1);
            zone.offsetMin = new Vector2(48, 40);
            zone.offsetMax = new Vector2(-24, -40);

            // Violet block with halftone, tilted behind everything
            var block = UIFactory.Card(zone, Theme.Violet, Theme.ShadowLg, "Block");
            block.offsetMin = new Vector2(30, 70);
            block.offsetMax = new Vector2(-10, -40);
            UIFactory.Tilt(block, 2f);
            var dots = UIFactory.Pattern(block, Theme.Halftone, 20f, 0.18f);
            UIFactory.Stretch(dots.rectTransform, Theme.Border);

            // Title stack: three words, three treatments
            var park = UIFactory.Badge(zone, "Park", Theme.Yellow, Theme.H1, -3f);
            UIFactory.Place(park, new Vector2(0, 1), new Vector2(330, 120), new Vector2(0, -10));

            var walk = UIFactory.Outlined(zone, "Walk", Theme.Display, Theme.Violet, TextAnchor.MiddleLeft);
            UIFactory.Place(walk.rectTransform, new Vector2(0, 1), new Vector2(560, 150), new Vector2(60, -140));

            var through = UIFactory.Badge(zone, "Through", Theme.Red, Theme.H1, 2f);
            UIFactory.Place(through, new Vector2(0, 1), new Vector2(470, 120), new Vector2(40, -300));

            // Subtitle card
            var sub = UIFactory.Card(zone, Theme.White, Theme.ShadowMd, "Subtitle");
            UIFactory.Place(sub, new Vector2(0, 1), new Vector2(540, 150), new Vector2(70, -470));
            UIFactory.Tilt(sub, -1f);
            var subText = UIFactory.Label(sub,
                world ? "Point at a button and pull the trigger. Walk the model, hop between 360° renders."
                      : "Walk the model, hop between 360° renders, or drop the design onto the real site with AR.",
                Theme.Small + 2, TextAnchor.MiddleLeft);
            UIFactory.Stretch(subText.rectTransform, 20);

            // Mode badges along the bottom
            var vr = UIFactory.Badge(zone, "VR", Theme.Red, Theme.H3, -6f);
            UIFactory.Place(vr, new Vector2(0, 0), new Vector2(120, 80), new Vector2(70, 40));
            var t360 = UIFactory.Badge(zone, "360°", Theme.Yellow, Theme.H3, 4f);
            UIFactory.Place(t360, new Vector2(0, 0), new Vector2(150, 80), new Vector2(220, 70));
            var ar = UIFactory.Badge(zone, "AR", Theme.White, Theme.H3, -3f);
            UIFactory.Place(ar, new Vector2(0, 0), new Vector2(120, 80), new Vector2(400, 30));

            // Floating shapes
            UIFactory.Star(zone, 150, Theme.Yellow, new Vector2(1, 1), new Vector2(10, 20));
            UIFactory.Circle(zone, 90, Theme.Red, new Vector2(1, 0), new Vector2(-30, 150));
            var square = UIFactory.Card(zone, Theme.Ink, Vector2.zero, "Square");
            UIFactory.Place(square, new Vector2(1, 0), new Vector2(56, 56), new Vector2(-150, 60));
            UIFactory.Tilt(square, 12f);
        }
    }
}
