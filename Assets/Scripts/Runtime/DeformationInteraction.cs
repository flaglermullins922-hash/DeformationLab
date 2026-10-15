// =============================================================================
// DeformationInteraction.cs —— 交互层：轨道相机 + FFD 控制点拾取 / 拖拽
//
// 相机：球坐标（yaw 水平角 / pitch 俯仰角 / distance 距离），中键旋转、
//       右键平移、滚轮缩放 —— 与 BezierCurveLab 的 CurveInteraction 一致。
//
// FFD 控制点拾取：屏幕空间投影 + 像素阈值（纯数学，不用 Physics.Raycast，
//       控制点不需要碰撞体）。拖拽在"过控制点、垂直于相机视线的平面"上进行，
//       所以鼠标往哪拖、控制点就往哪走，直观不别扭。
// =============================================================================

using UnityEngine;
using UnityEngine.EventSystems;

namespace DeformationLab
{
    public sealed class DeformationInteraction : MonoBehaviour
    {
        [Header("引用")]
        public DeformationController controller;
        public Camera cam;

        [Header("相机")]
        public float distance = 16f;
        public float yaw = 30f;
        public float pitch = 25f;
        public float orbitSpeed = 0.25f;
        public float zoomSpeed = 3.0f;
        public float panSpeed = 0.0022f;
        public float minDistance = 2f;
        public float maxDistance = 120f;

        [Header("拾取")]
        public float pickRadiusPx = 26f;

        Vector3 _target = Vector3.zero;
        int _hoverIndex = -1;
        int _dragIndex = -1;
        Vector3 _dragOffset;
        Vector2 _rightDownPos;
        bool _rightDragged;

        void Start()
        {
            if (cam == null) cam = Camera.main;
            if (controller == null) controller = FindObjectOfType<DeformationController>();
            FrameModel();
        }

        void Update()
        {
            if (controller == null) return;
            if (cam == null) cam = Camera.main;

            CameraControl();
            HandlePicking();
            ApplyCamera();
            RefreshHandleStates();
        }

        // =========================================================================
        //  相机
        // =========================================================================
        void CameraControl()
        {
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f && !overUI)
                distance = Mathf.Clamp(distance - scroll * zoomSpeed * (0.3f + distance * 0.05f),
                                       minDistance, maxDistance);

            if (!overUI && Input.GetMouseButton(2))
            {
                yaw += Input.GetAxis("Mouse X") * orbitSpeed * 20f;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * orbitSpeed * 20f, 5f, 89f);
            }

            if (Input.GetMouseButtonDown(1)) { _rightDownPos = Input.mousePosition; _rightDragged = false; }
            if (Input.GetMouseButton(1) && !overUI)
            {
                Vector2 d = (Vector2)Input.mousePosition - _rightDownPos;
                if (d.magnitude > 6f) _rightDragged = true;
                if (_rightDragged)
                {
                    Vector3 right = cam.transform.right, up = cam.transform.up;
                    _target -= (right * d.x + up * d.y) * panSpeed * distance;
                    _rightDownPos = Input.mousePosition;
                }
            }
        }

        void ApplyCamera()
        {
            if (cam == null) return;
            float yr = yaw * Mathf.Deg2Rad, pr = pitch * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(yr) * Mathf.Cos(pr), Mathf.Sin(pr), -Mathf.Cos(yr) * Mathf.Cos(pr));
            cam.transform.position = _target + dir * distance;
            cam.transform.LookAt(_target, Vector3.up);
        }

        /// <summary>按模型包围盒自动取景（模型多大都能自动框好）。</summary>
        public void FrameModel()
        {
            if (controller == null || controller.model == null || !controller.model.IsLoaded) return;
            var b = controller.model.ModelBounds;
            _target = b.center;
            float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
            distance = maxDim * 2.2f + 1f;
            yaw = 30f; pitch = 25f;
            ApplyCamera();
        }

        public void ResetView()
        {
            FrameModel();
        }

        // =========================================================================
        //  FFD 控制点拾取 / 拖拽
        // =========================================================================
        void HandlePicking()
        {
            bool ffdMode = controller.mode == DeformMode.FFD && controller.ffdHandles != null;
            if (!ffdMode) { _hoverIndex = -1; _dragIndex = -1; return; }

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Vector3 mouse = Input.mousePosition;

            if (Input.GetMouseButtonDown(0) && !overUI)
            {
                int hit = PickHandle(mouse);
                if (hit >= 0)
                {
                    _dragIndex = hit;
                    Vector3 hp = controller.ffdHandles[hit].transform.position;
                    Plane plane = new Plane(-cam.transform.forward, hp);
                    Ray ray = cam.ScreenPointToRay(mouse);
                    if (plane.Raycast(ray, out float enter))
                        _dragOffset = hp - ray.GetPoint(enter);
                }
            }

            if (Input.GetMouseButtonUp(0)) _dragIndex = -1;

            if (_dragIndex >= 0 && Input.GetMouseButton(0) && _dragIndex < controller.ffdHandles.Count)
            {
                Vector3 hp = controller.ffdHandles[_dragIndex].transform.position;
                Plane plane = new Plane(-cam.transform.forward, hp);
                Ray ray = cam.ScreenPointToRay(mouse);
                if (plane.Raycast(ray, out float enter))
                {
                    Vector3 p = ray.GetPoint(enter) + _dragOffset;
                    controller.MoveFfdHandle(_dragIndex, p);
                }
            }

            _hoverIndex = _dragIndex >= 0 ? _dragIndex : PickHandle(mouse);
        }

        /// <summary>屏幕空间拾取：取离鼠标最近且在阈值内的控制点。</summary>
        int PickHandle(Vector3 mousePos)
        {
            if (cam == null || controller == null || controller.ffdHandles == null) return -1;
            int best = -1;
            float bestDist = pickRadiusPx;
            for (int i = 0; i < controller.ffdHandles.Count; i++)
            {
                var h = controller.ffdHandles[i];
                if (h == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(h.transform.position);
                if (sp.z <= 0f) continue;   // 在相机背面
                float d = Vector2.Distance(new Vector2(sp.x, sp.y), new Vector2(mousePos.x, mousePos.y));
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        void RefreshHandleStates()
        {
            if (controller == null || controller.ffdHandles == null) return;
            for (int i = 0; i < controller.ffdHandles.Count; i++)
            {
                var h = controller.ffdHandles[i];
                if (h == null) continue;
                DeformationFactory.SetHandleSelected(h, i == _dragIndex || i == _hoverIndex);
            }
        }

        public void ClearSelection()
        {
            _hoverIndex = -1;
            _dragIndex = -1;
        }
    }
}
