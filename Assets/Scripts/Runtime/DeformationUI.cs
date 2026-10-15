// =============================================================================
// DeformationUI.cs —— 运行时用代码搭建 UGUI 界面 + 逻辑接线
//
// 左侧面板包含：变形模式按钮 / 主轴与弯曲方向选择 / 各模式参数滑块 /
// FFD 分辨率选择 / 内置演示模型按钮 / 导入 OBJ 输入框 / 实时信息面板。
// 全部控件在运行时生成（与 QuaternionLab / BezierCurveLab 风格一致），
// 字体用内置 LegacyRuntime.ttf（中文渲染正常）。
// =============================================================================

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DeformationLab
{
    public sealed class DeformationUI : MonoBehaviour
    {
        public DeformationController controller;

        Font _font;
        Canvas _canvas;

        // 控件引用
        Button[] _modeBtns;
        Button[] _axisBtns;
        Button[] _bendDirBtns;
        Button[] _resBtns;
        Slider _taperSlider, _twistSlider, _bendSlider, _bendCenterSlider;
        Text _taperVal, _twistVal, _bendVal, _bendCenterVal;
        Text _infoText;
        InputField _pathInput;
        RectTransform _taperRow, _twistRow, _bendRow, _bendCenterRow, _ffdRow, _bendDirRow;
        Text _modelListText;

        static readonly Color C_BG = new Color(0.10f, 0.11f, 0.14f, 0.94f);
        static readonly Color C_ACCENT = new Color(0.30f, 0.72f, 0.85f);
        static readonly Color C_BTN = new Color(0.18f, 0.21f, 0.26f, 0.9f);
        static readonly Color C_BTN_SEL = new Color(0.95f, 0.58f, 0.16f, 0.95f);
        static readonly Color C_TEXT = new Color(0.92f, 0.94f, 0.96f);
        static readonly Color C_TEXT_DIM = new Color(0.62f, 0.67f, 0.72f);

        void Start()
        {
            if (controller == null) controller = FindObjectOfType<DeformationController>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
            ScanStreamingModels();
            Refresh();
            RefreshMode();
        }

        // =========================================================================
        //  搭建
        // =========================================================================
        void Build()
        {
            // ---- Canvas + EventSystem ----
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // ---- 面板 ----
            var panel = CreateRect("Panel", canvasGo.transform, 20, 20, 390, 1040, C_BG);

            // 标题
            AddLabel(panel, "全局变形演示", 18, 16, 25, C_TEXT, FontStyle.Bold);
            AddLabel(panel, "Global Deformation Lab · 锥化/扭曲/弯曲/FFD", 18, 52, 15, C_TEXT_DIM, FontStyle.Normal);
            AddDivider(panel, 18, 80, 354);

            // ---- 模式按钮 ----
            string[] modeNames = { "锥化", "扭曲", "弯曲", "FFD", "重置" };
            _modeBtns = new Button[5];
            for (int i = 0; i < 5; i++)
            {
                int mi = i;
                _modeBtns[i] = AddButton(panel, modeNames[i], 18 + i * 72, 92, 68, 36,
                    () => OnModeButton(mi));
            }

            // ---- 主轴选择 ----
            AddLabel(panel, "变形主轴", 18, 140, 15, C_TEXT_DIM, FontStyle.Normal);
            _axisBtns = new Button[3];
            string[] axisNames = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                int ai = i;
                _axisBtns[i] = AddButton(panel, axisNames[i], 18 + i * 72, 162, 68, 32,
                    () => controller.SetAxis((DeformAxis)ai));
            }

            // ---- 弯曲方向（仅 Bend 模式显示）----
            _bendDirRow = CreateRect("BendDirRow", panel, 0, 200, 390, 34);
            AddLabel(_bendDirRow, "弯曲方向", 18, 4, 15, C_TEXT_DIM, FontStyle.Normal);
            _bendDirBtns = new Button[2];
            string[] dirNames = { "X", "Z" };
            for (int i = 0; i < 2; i++)
            {
                int di = i == 0 ? (int)DeformAxis.X : (int)DeformAxis.Z;
                _bendDirBtns[i] = AddButton(_bendDirRow, dirNames[i], 92 + i * 56, 0, 52, 32,
                    () => controller.SetBendDir((DeformAxis)di));
            }

            // ---- 参数滑块行 ----
            _taperRow = CreateRect("TaperRow", panel, 0, 238, 390, 46);
            _taperSlider = AddSlider(_taperRow, "锥化系数 k", 18, -1f, 1f, -0.6f, out _taperVal,
                v => controller.SetTaper(v));

            _twistRow = CreateRect("TwistRow", panel, 0, 238, 390, 46);
            _twistSlider = AddSlider(_twistRow, "扭转角度", 18, 0f, 360f, 180f, out _twistVal,
                v => controller.SetTwist(v), "°");

            _bendRow = CreateRect("BendRow", panel, 0, 238, 390, 46);
            _bendSlider = AddSlider(_bendRow, "弯曲角度", 18, 0f, 180f, 90f, out _bendVal,
                v => controller.SetBend(v), "°");

            _bendCenterRow = CreateRect("BendCenterRow", panel, 0, 238, 390, 46);
            _bendCenterSlider = AddSlider(_bendCenterRow, "弯曲中心", 18, 0f, 1f, 0.5f, out _bendCenterVal,
                v => controller.SetBendCenter(v));

            _ffdRow = CreateRect("FfdRow", panel, 0, 238, 390, 46);
            AddLabel(_ffdRow, "格子分辨率 n", 18, 4, 15, C_TEXT_DIM, FontStyle.Normal);
            _resBtns = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int r = i + 2;
                _resBtns[i] = AddButton(_ffdRow, r.ToString(), 140 + i * 56, 0, 52, 32,
                    () => controller.SetFfdResolution(r));
            }
            AddLabel(_ffdRow, "拖蓝色控制点变形 · R重置格子", 18, 34, 13, C_ACCENT, FontStyle.Normal);

            // ---- 内置演示模型 ----
            AddLabel(panel, "演示模型（内置）", 18, 292, 15, C_TEXT_DIM, FontStyle.Normal);
            string[] modelNames = { "圆柱", "立方", "球体" };
            string[] modelKeys = { "cylinder", "box", "sphere" };
            for (int i = 0; i < 3; i++)
            {
                string k = modelKeys[i];
                AddButton(panel, modelNames[i], 18 + i * 90, 314, 86, 32,
                    () => controller.LoadProcedural(k));
            }

            // ---- 导入 OBJ ----
            AddLabel(panel, "导入 OBJ 文件（绝对路径或文件名）", 18, 358, 15, C_TEXT_DIM, FontStyle.Normal);
            _pathInput = AddInput(panel, 18, 380, 266, 36);
            _pathInput.text = "";
            AddButton(panel, "加载", 292, 380, 80, 36, OnLoadObj);

            AddLabel(panel, "内置 .obj（StreamingAssets/models）：", 18, 428, 14, C_TEXT_DIM, FontStyle.Normal);
            _modelListText = AddLabel(panel, "(扫描中…)", 18, 452, 14, C_ACCENT, FontStyle.Normal);
            _modelListText.GetComponent<RectTransform>().sizeDelta = new Vector2(354, 80);

            // ---- 信息面板 ----
            AddDivider(panel, 18, 642, 354);
            _infoText = AddLabel(panel, "", 18, 656, 16, C_TEXT, FontStyle.Normal);
            _infoText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _infoText.GetComponent<RectTransform>().sizeDelta = new Vector2(354, 360);

            // ---- 操作提示 ----
            AddLabel(panel, "中键旋转 · 右键平移 · 滚轮缩放", 18, 1000, 14, C_TEXT_DIM, FontStyle.Normal);
        }

        // =========================================================================
        //  回调
        // =========================================================================
        void OnModeButton(int i)
        {
            if (i == 4) { controller.ResetDeformation(); return; }   // 重置
            controller.SetMode((DeformMode)(i + 1));                  // 锥化=1 扭曲=2 弯曲=3 FFD=4
        }

        void OnLoadObj()
        {
            string path = _pathInput != null ? _pathInput.text.Trim() : "";
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path)) controller.LoadObjFile(path);
            else controller.LoadStreamingObj(path);   // 当作 StreamingAssets/models 下的文件名
        }

        void ScanStreamingModels()
        {
            if (_modelListText == null) return;
            string dir = Path.Combine(Application.streamingAssetsPath, "models");
            if (!Directory.Exists(dir)) { _modelListText.text = "(无)"; return; }
            var files = Directory.GetFiles(dir, "*.obj");
            if (files.Length == 0) { _modelListText.text = "(无)"; return; }
            var sb = new System.Text.StringBuilder();
            foreach (string f in files) sb.Append("· ").Append(Path.GetFileName(f)).Append("   ");
            _modelListText.text = sb.ToString();
        }

        // =========================================================================
        //  刷新
        // =========================================================================
        public void Refresh()
        {
            if (_infoText == null || controller == null || controller.model == null || !controller.model.IsLoaded)
                return;
            var m = controller.model;
            var b = m.ModelBounds;
            var sb = new System.Text.StringBuilder();
            sb.Append("顶点: ").Append(m.VertexCount)
              .Append("   三角面: ").Append(m.TriangleCount)
              .Append("\n子网格: ").Append(m.PartCount)
              .Append("   OBJ原始顶点: ").Append(m.SourceVertexCount > 0 ? m.SourceVertexCount.ToString() : "-")
              .Append("\n\n包围盒尺寸\n  X ").Append(b.size.x.ToString("F2"))
              .Append("   Y ").Append(b.size.y.ToString("F2"))
              .Append("   Z ").Append(b.size.z.ToString("F2"))
              .Append("\n\n模式: ").Append(ModeName())
              .Append("\n主轴: ").Append(controller.axis)
              .Append("\n\n参数:");
            switch (controller.mode)
            {
                case DeformMode.Taper: sb.Append(" 锥化 k=").Append(controller.taperAmount.ToString("F2")); break;
                case DeformMode.Twist: sb.Append(" 扭转 ").Append(controller.twistDeg.ToString("F0")).Append("°"); break;
                case DeformMode.Bend:  sb.Append(" 弯曲 ").Append(controller.bendDeg.ToString("F0")).Append("°  中心 ").Append(controller.bendCenter.ToString("F2")); break;
                case DeformMode.FFD:   sb.Append(" 格子 ").Append(controller.ffdResolution).Append("×").Append(controller.ffdResolution).Append("×").Append(controller.ffdResolution); break;
                default: sb.Append(" 无"); break;
            }
            _infoText.text = sb.ToString();
        }

        public void RefreshMode()
        {
            if (controller == null || _modeBtns == null) return;
            int active = (int)controller.mode - 1;   // 锥化=0 扭曲=1 弯曲=2 FFD=3

            for (int i = 0; i < _modeBtns.Length; i++)
                if (_modeBtns[i] != null)
                    _modeBtns[i].GetComponent<Image>().color = (i == active) ? C_BTN_SEL : C_BTN;

            for (int i = 0; i < _axisBtns.Length; i++)
                if (_axisBtns[i] != null)
                    _axisBtns[i].GetComponent<Image>().color = ((int)controller.axis == i) ? C_ACCENT : C_BTN;

            if (_bendDirBtns != null)
                for (int i = 0; i < _bendDirBtns.Length; i++)
                {
                    var d = i == 0 ? DeformAxis.X : DeformAxis.Z;
                    _bendDirBtns[i].GetComponent<Image>().color = (controller.bendDir == d) ? C_ACCENT : C_BTN;
                }

            if (_resBtns != null)
                for (int i = 0; i < _resBtns.Length; i++)
                    _resBtns[i].GetComponent<Image>().color = (controller.ffdResolution == i + 2) ? C_ACCENT : C_BTN;

            _taperRow.gameObject.SetActive(controller.mode == DeformMode.Taper);
            _twistRow.gameObject.SetActive(controller.mode == DeformMode.Twist);
            _bendRow.gameObject.SetActive(controller.mode == DeformMode.Bend);
            _bendCenterRow.gameObject.SetActive(controller.mode == DeformMode.Bend);
            _bendDirRow.gameObject.SetActive(controller.mode == DeformMode.Bend);
            _ffdRow.gameObject.SetActive(controller.mode == DeformMode.FFD);

            Refresh();
        }

        string ModeName()
        {
            switch (controller.mode)
            {
                case DeformMode.Taper: return "锥化 Taper";
                case DeformMode.Twist: return "扭曲 Twist";
                case DeformMode.Bend: return "弯曲 Bend";
                case DeformMode.FFD: return "FFD 自由变形";
                default: return "无（原始模型）";
            }
        }

        // =========================================================================
        //  UGUI 控件构造 helper
        // =========================================================================
        RectTransform CreateRect(string name, Transform parent, float x, float y, float w, float h, Color? bg = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0, 1);
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            if (bg.HasValue) go.AddComponent<Image>().color = bg.Value;
            return rt;
        }

        Text AddLabel(Transform parent, string text, float x, float y, int size, Color color, FontStyle style)
        {
            var rt = CreateRect("Label", parent, x, y, 340, size + 8);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font; t.fontSize = size; t.color = color; t.fontStyle = style;
            t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = text;
            return t;
        }

        void AddDivider(Transform parent, float x, float y, float w)
        {
            CreateRect("Divider", parent, x, y, w, 1, new Color(0.30f, 0.34f, 0.40f, 0.6f));
        }

        Button AddButton(Transform parent, string label, float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0, 1);
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            var img = go.GetComponent<Image>();
            img.color = C_BTN;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var labelGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var t = labelGo.GetComponent<Text>();
            t.font = _font; t.fontSize = 16; t.color = C_TEXT;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = label;
            return btn;
        }

        Slider AddSlider(Transform parent, string title, float x, float min, float max, float val,
                         out Text valueText, UnityEngine.Events.UnityAction<float> onChange, string suffix = "")
        {
            AddLabel(parent, title, x, 2, 15, C_TEXT_DIM, FontStyle.Normal);

            // 滑轨背景
            var bg = CreateRect("SliderBG", parent, x, 24, 230, 10, new Color(0.30f, 0.34f, 0.40f));

            // fill
            var fill = CreateRect("Fill", bg, 0, 0, 230, 10, C_ACCENT);
            fill.anchorMin = new Vector2(0, 0.5f); fill.anchorMax = new Vector2(0, 0.5f);
            fill.pivot = new Vector2(0, 0.5f);
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = new Vector2(230, 10);

            // handle
            var handle = CreateRect("Handle", bg, 0, 0, 22, 22, C_TEXT);
            handle.anchorMin = new Vector2(0, 0.5f); handle.anchorMax = new Vector2(0, 0.5f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.anchoredPosition = Vector2.zero;
            handle.sizeDelta = new Vector2(22, 22);

            // slider 组件挂 bg 上
            var slider = bg.gameObject.AddComponent<Slider>();
            slider.minValue = min; slider.maxValue = max; slider.value = val;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.onValueChanged.AddListener(onChange);

            // 值文本
            var vt = AddLabel(parent, val.ToString("F2") + suffix, x + 240, 2, 15, C_TEXT, FontStyle.Normal);
            valueText = vt;
            slider.onValueChanged.AddListener(v =>
            {
                vt.text = (suffix == "°" ? v.ToString("F0") : v.ToString("F2")) + suffix;
                Refresh();
            });

            // 初始文字
            vt.text = (suffix == "°" ? val.ToString("F0") : val.ToString("F2")) + suffix;
            return slider;
        }

        InputField AddInput(Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0, 1);
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            go.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.22f, 0.9f);

            var input = go.GetComponent<InputField>();
            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 0); trt.offsetMax = new Vector2(-10, 0);
            var t = textGo.GetComponent<Text>();
            t.font = _font; t.fontSize = 16; t.color = C_TEXT;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.supportRichText = false;
            input.textComponent = t;
            return input;
        }
    }
}
