using UnityEngine;

namespace ProbablyAI
{
    public sealed class AssistantSettings
    {
        public ConfigEntry<bool> Visible { get; }
        public ConfigEntry<float> Height { get; }
        public ConfigEntry<float> Left { get; }
        public ConfigEntry<float> Bottom { get; }
        public ConfigEntry<float> Opacity { get; }
        public ConfigEntry<KeyCode> ToggleKey { get; }
        public ConfigEntry<KeyCode> PanelKey { get; }

        public AssistantSettings(ModConfig config)
        {
            Visible = config.Define("portrait_visible", true, "立绘", () => "显示小鲸立绘");
            Height = config.Define("portrait_height", 680f, "立绘", () => "立绘高度（1080p 基准）", 240f, 1000f);
            Left = config.Define("portrait_left", 20f, "立绘", () => "距离左侧", 0f, 900f);
            Bottom = config.Define("portrait_bottom", 40f, "立绘", () => "距离底部", 0f, 500f);
            Opacity = config.Define("portrait_opacity", 1f, "立绘", () => "不透明度", 0.2f, 1f);
            ToggleKey = config.DefineEnum("toggle_key", KeyCode.F6, "快捷键", () => "显示 / 隐藏立绘");
            PanelKey = config.DefineEnum("panel_key", KeyCode.F7, "快捷键", () => "打开 / 关闭连接配置");
        }
    }
}
