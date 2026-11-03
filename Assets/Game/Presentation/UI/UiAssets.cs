using System.Collections.Generic;
using Immortal.Core;
using Immortal.Core.Narrative;
using UnityEngine;
using UnityEngine.UIElements;

namespace Immortal.Presentation.UI
{
    /// <summary>
    /// Картинки интерфейса по соглашению имён (если файла нет — интерфейс просто без картинки):
    /// Resources/Portraits/&lt;rec_id | leader_&lt;nation&gt; | leader | hero | companion | npc&gt;,
    /// Resources/Icons/Abilities/&lt;id&gt;, Icons/Items/&lt;id&gt;, Icons/Buildings/&lt;id&gt;, Icons/Techs/&lt;id&gt;.
    /// </summary>
    public static class UiAssets
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static Texture2D Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!Cache.TryGetValue(path, out var t)) { t = Resources.Load<Texture2D>(path); Cache[path] = t; }
            return t;
        }

        public static Texture2D Icon(string category, string id) => id == null ? null : Load($"Icons/{category}/{id}");

        public static Texture2D Portrait(params string[] keys)
        {
            foreach (var k in keys) { var t = Load("Portraits/" + k); if (t != null) return t; }
            return null;
        }

        /// <summary>Портрет собеседника в текущем диалоге.</summary>
        public static Texture2D DialoguePortrait(GameSession s)
        {
            var ctx = s.State.Narrative.Active;
            if (ctx == null) return null;
            var npc = s.Npc(ctx.NpcId);
            if (npc != null) return Portrait(npc.RecruitDefId, npc.IsDescendant ? "descendant" : "npc");
            var comp = s.Companion(ctx.CompanionId);
            if (comp != null) return Portrait(comp.RecruitDefId, "companion");
            var n = s.Nation(ctx.NationId);
            if (n != null) return Portrait("leader_" + n.DefId, "leader");
            return Portrait(ctx.DialogueId, "narrator");
        }

        public static Image ImageOf(Texture2D t, float size)
        {
            var img = new Image { image = t, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.style.width = size; img.style.height = size;
            img.style.marginRight = 8; img.style.flexShrink = 0;
            return img;
        }
    }
}
