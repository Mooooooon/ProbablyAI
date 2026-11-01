using UnityEngine;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI
{
    public sealed class AssistantSettings
    {
        private readonly ModConfig config;
        public ConfigEntry<bool> Visible { get; private set; }
        public ConfigEntry<float> Height { get; private set; }
        public ConfigEntry<float> Left { get; private set; }
        public ConfigEntry<float> Bottom { get; private set; }
        public ConfigEntry<float> Opacity { get; private set; }
        public ConfigEntry<bool> BreathEnabled { get; private set; }
        public ConfigEntry<float> BreathAmplitude { get; private set; }
        public ConfigEntry<float> BreathPeriod { get; private set; }
        public ConfigEntry<KeyCode> ToggleKey { get; private set; }
        public ConfigEntry<KeyCode> PanelKey { get; private set; }
        public ConfigEntry<bool> VisitorWarnings { get; private set; }

        public AssistantSettings(ModConfig config)
        {
            this.config = config;
            RefreshLanguage();
        }

        internal void RefreshLanguage()
        {
            // Redefine through the native API so section headings follow language too.
            // Native Set persists values; Define reloads those same values by stable key.
            Visible = config.Define("portrait_visible", true, T("立绘"), () => T("显示小鲸立绘"));
            Height = config.Define("portrait_height", 680f, T("立绘"), () => T("立绘高度（1080p 基准）"), 240f, 1000f);
            Left = config.Define("portrait_left", 20f, T("立绘"), () => T("距离左侧"), 0f, 900f);
            Bottom = config.Define("portrait_bottom", 40f, T("立绘"), () => T("距离底部"), 0f, 500f);
            Opacity = config.Define("portrait_opacity", 1f, T("立绘"), () => T("不透明度"), 0.2f, 1f);
            BreathEnabled = config.Define("portrait_breath", true, T("立绘"), () => T("呼吸动画"));
            BreathAmplitude = config.Define("portrait_breath_amplitude", 6f, T("立绘"), () => T("呼吸幅度（1080p 基准）"), 0f, 30f);
            BreathPeriod = config.Define("portrait_breath_period", 3.6f, T("立绘"), () => T("呼吸周期（秒）"), 1.5f, 8f);
            ToggleKey = config.DefineEnum("toggle_key", KeyCode.F6, T("快捷键"), () => T("显示 / 隐藏立绘"));
            PanelKey = config.DefineEnum("panel_key", KeyCode.F7, T("快捷键"), () => T("打开 / 关闭连接配置"));
            VisitorWarnings = config.Define("visitor_warnings", true, T("客人提醒"), () => T("客人到店时小声提醒通缉 / 改造体"));
        }
    }
}
