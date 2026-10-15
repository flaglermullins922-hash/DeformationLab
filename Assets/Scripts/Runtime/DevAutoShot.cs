// =============================================================================
// DevAutoShot.cs —— 开发辅助：命令行自动截图（验收用）
//
// 正常运行完全无感（不带 -shot 参数时立刻返回）。带参数时用于在批处理里
// 验证"某种模式下的真实画面"：
//   DeformationLab.exe -shot D:/out/taper.png -mode taper -amount -0.6
//   DeformationLab.exe -shot D:/out/ffd.png  -mode ffd -ffdres 4 -ffdmove
// 支持参数：-shot / -model / -mode / -axis / -amount / -bendcenter / -ffdres / -ffdmove
// 参照 BezierCurveLab 的 DevAutoShot 思路：WaitForEndOfFrame + ReadPixels 连 UGUI 一起拍。
// =============================================================================

using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace DeformationLab
{
    public sealed class DevAutoShot : MonoBehaviour
    {
        IEnumerator Start()
        {
            yield return null;   // 等一帧，确保 UI / Controller 的 Start 都已执行完
            string shot = GetArg("-shot");
            if (string.IsNullOrEmpty(shot)) yield break;   // 正常模式，不截图

            var controller = FindObjectOfType<DeformationController>();

            // ---- 模型 ----
            string model = GetArg("-model");
            if (!string.IsNullOrEmpty(model) && controller != null)
            {
                string m = model.ToLowerInvariant();
                if (m.EndsWith(".obj")) controller.LoadObjFile(model);
                else if (m == "box" || m == "sphere" || m == "cylinder") controller.LoadProcedural(m);
                yield return new WaitForSeconds(0.4f);
            }

            // ---- 轴 / 模式 / 参数 ----
            if (controller != null)
            {
                string axis = GetArg("-axis");
                if (!string.IsNullOrEmpty(axis)) controller.SetAxis(ParseAxis(axis));

                string mode = GetArg("-mode");
                if (!string.IsNullOrEmpty(mode))
                {
                    string mm = mode.ToLowerInvariant();
                    switch (mm)
                    {
                        case "taper":
                            controller.SetTaper(GetFloat("-amount", -0.6f));
                            controller.SetMode(DeformMode.Taper);
                            break;
                        case "twist":
                            controller.SetTwist(GetFloat("-amount", 180f));
                            controller.SetMode(DeformMode.Twist);
                            break;
                        case "bend":
                            controller.SetBend(GetFloat("-amount", 90f));
                            controller.SetBendCenter(GetFloat("-bendcenter", 0.5f));
                            controller.SetMode(DeformMode.Bend);
                            break;
                        case "ffd":
                            controller.SetFfdResolution((int)GetFloat("-ffdres", 4f));
                            controller.SetMode(DeformMode.FFD);
                            if (GetArg("-ffdmove") != null) ApplyFfdDemo(controller);
                            break;
                        default:
                            controller.SetMode(DeformMode.None);
                            break;
                    }
                }
            }

            // ---- 等画面稳定 ----
            for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();

            // ---- 截图（连 UGUI 一起）----
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            string dir = Path.GetDirectoryName(shot);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(shot, tex.EncodeToPNG());
            Debug.Log("[DevAutoShot] 截图已保存: " + shot + "  " + Screen.width + "x" + Screen.height);

            yield return new WaitForSeconds(0.3f);
            Application.Quit();
        }

        /// <summary>FFD 演示变形：把最顶一层控制点向外扩 + 抬高，形成"张开的喇叭口"。</summary>
        static void ApplyFfdDemo(DeformationController c)
        {
            if (c.ffd == null) return;
            int n = c.ffd.n;
            float cx = c.ffd.origin.x + c.ffd.size.x * 0.5f;
            float cz = c.ffd.origin.z + c.ffd.size.z * 0.5f;
            for (int i = 0; i < n; i++)
                for (int k = 0; k < n; k++)
                {
                    int idx = FfdLattice.IndexOf(i, n - 1, k, n);
                    Vector3 p = c.ffd.current[i, n - 1, k];
                    Vector3 dir = new Vector3(p.x - cx, 0f, p.z - cz);
                    if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
                    dir.Normalize();
                    c.MoveFfdHandle(idx, p + dir * 0.9f + Vector3.up * 0.7f);
                }
        }

        static DeformAxis ParseAxis(string s)
        {
            if (string.IsNullOrEmpty(s)) return DeformAxis.Y;
            char ch = char.ToUpperInvariant(s[0]);
            if (ch == 'X') return DeformAxis.X;
            if (ch == 'Z') return DeformAxis.Z;
            return DeformAxis.Y;
        }

        static string GetArg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == key) return args[i + 1];
            return null;
        }

        static float GetFloat(string key, float def)
        {
            string v = GetArg(key);
            float r;
            return v != null && float.TryParse(v, out r) ? r : def;
        }
    }
}
