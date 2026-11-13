using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Civilization
{
    internal sealed class MapCameraControls : System.IDisposable
    {
        private const float MinimumZoom = 4f;
        private const float WheelSensitivity = 0.16f;
        private static readonly Plane Ground = new(Vector3.up, Vector3.zero);

        private readonly Camera camera;
        private readonly TrialHud hud;
        private readonly float mapHalfSize;
        private readonly float maximumZoom;
        private ButtonControl dragButton;
        private Vector2 mouseAnchor;
        private int trackedTouches;
        private int firstTouchId, secondTouchId;
        private double firstTouchStart, secondTouchStart;
        private bool touchBlocked;
        private Vector2 touchAnchor;
        private float touchSpan;

        internal MapCameraControls(Camera camera, TrialHud hud, float mapSize)
        {
            this.camera = camera;
            this.hud = hud;
            mapHalfSize = mapSize * 0.5f;
            maximumZoom = mapSize * 0.82f;

            Quaternion angle = Quaternion.Euler(53f, 0f, 0f);
            Vector3 direction = angle * Vector3.forward;
            camera.transform.SetPositionAndRotation(direction * (36f / direction.y), angle);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Min(mapSize, 60f) * 0.62f;
            camera.clearFlags = CameraClearFlags.SolidColor;

            EnhancedTouchSupport.Enable();
        }

        internal void Update()
        {
            if (UpdateTouch())
            {
                dragButton = null;
                return;
            }

            UpdateMouse();
        }

        private bool CanStart(Vector2 point) => camera.pixelRect.Contains(point) && !hud.BlocksCamera(point);

        private bool UpdateTouch()
        {
            Touch first = default;
            Touch second = default;
            int count = 0;
            foreach (Touch touch in Touch.activeTouches)
            {
                if (!touch.inProgress)
                    continue;
                count++;
                if (count == 1)
                    first = touch;
                else if (count == 2)
                    second = touch;
                else
                {
                    trackedTouches = 0;
                    return true;
                }
            }

            if (count == 0)
            {
                trackedTouches = 0;
                return false;
            }

            if (count == 2 && first.touchId > second.touchId)
                (first, second) = (second, first);

            Vector2 midpoint = first.screenPosition;
            float span = 0f;
            if (count == 2)
            {
                midpoint = (first.screenPosition + second.screenPosition) * 0.5f;
                span = Vector2.Distance(first.screenPosition, second.screenPosition);
            }

            // Re-anchor when a finger enters or leaves, even if the device reuses its ID.
            bool changed = trackedTouches != count || firstTouchId != first.touchId || firstTouchStart != first.startTime
                || (count == 2 && (secondTouchId != second.touchId || secondTouchStart != second.startTime));
            if (changed)
            {
                trackedTouches = count;
                firstTouchId = first.touchId;
                firstTouchStart = first.startTime;
                if (count == 2)
                {
                    secondTouchId = second.touchId;
                    secondTouchStart = second.startTime;
                }
                touchBlocked = !CanStart(first.startScreenPosition) || (count == 2 && !CanStart(second.startScreenPosition));
            }
            else if (!touchBlocked && (midpoint != touchAnchor || span != touchSpan))
            {
                float zoom = span > 1f && touchSpan > 1f ? touchSpan / span : 1f;
                ApplyGesture(touchAnchor, midpoint, zoom);
            }

            touchAnchor = midpoint;
            touchSpan = span;
            return true;
        }

        private void UpdateMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                dragButton = null;
                return;
            }

            Vector2 position = mouse.position.ReadValue();
            if (dragButton != null && (!dragButton.isPressed || dragButton.device != mouse))
                dragButton = null;

            if (dragButton != null)
            {
                if (position != mouseAnchor)
                    ApplyGesture(mouseAnchor, position, 1f);
                mouseAnchor = position;
            }
            else if ((mouse.middleButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                && CanStart(position))
            {
                dragButton = mouse.middleButton.wasPressedThisFrame ? mouse.middleButton : mouse.rightButton;
                mouseAnchor = position;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f && CanStart(position))
                ApplyGesture(position, position, Mathf.Exp(-Mathf.Clamp(scroll, -4f, 4f) * WheelSensitivity));
        }

        private void ApplyGesture(Vector2 previousPoint, Vector2 currentPoint, float zoom)
        {
            Vector3 groundAnchor = GroundPoint(camera.ScreenPointToRay(previousPoint));
            float zoomLimit = Mathf.Max(maximumZoom, mapHalfSize / Mathf.Max(0.25f, camera.aspect));
            camera.orthographicSize = Mathf.Clamp(camera.orthographicSize * zoom, MinimumZoom, zoomLimit);
            Vector3 shift = groundAnchor - GroundPoint(camera.ScreenPointToRay(currentPoint));

            Vector3 focus = GroundPoint(camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)));
            float x = Mathf.Clamp(focus.x + shift.x, -mapHalfSize, mapHalfSize);
            float z = Mathf.Clamp(focus.z + shift.z, -mapHalfSize, mapHalfSize);
            camera.transform.position += new Vector3(x - focus.x, 0f, z - focus.z);
        }

        private static Vector3 GroundPoint(Ray ray)
        {
            Ground.Raycast(ray, out float distance);
            return ray.GetPoint(distance);
        }

        public void Dispose() => EnhancedTouchSupport.Disable();
    }
}
