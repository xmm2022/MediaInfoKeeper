namespace MediaInfoKeeper.Options.Store
{
    using System.Text.Json.Nodes;

    internal static class PluginOptionsJsonMigration
    {
        public static void MigrateLegacyEnhanceOptions(JsonObject root)
        {
            if (!(root?["Enhance"] is JsonObject enhance))
            {
                return;
            }

            CopyValue(enhance, "EnableStrmDirectRedirect", "EnableStrmVideoDirectRedirect");
            CopyValue(enhance, "EnableStrmDirectRedirect", "EnableStrmAudioDirectRedirect");
            CopyValue(enhance, "StrmDirectRedirectFollow302", "StrmVideoDirectRedirectFollow302");
            CopyValue(enhance, "StrmDirectRedirectFollow302", "StrmAudioDirectRedirectFollow302");
        }

        private static void CopyValue(JsonObject parent, string sourceName, string targetName)
        {
            if (parent == null || parent[targetName] != null)
            {
                return;
            }

            var sourceValue = parent[sourceName];
            if (sourceValue == null)
            {
                return;
            }

            parent[targetName] = JsonNode.Parse(sourceValue.ToJsonString());
        }
    }
}
