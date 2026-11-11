using UnityEngine;

namespace GameVerse.Spike
{
    /// <summary>
    /// Prompt 0.7 web performance spike (throwaway, deleted with ADR-0002): drives the car around the oval track the
    /// editor generated (same radii) and keeps a chase camera behind it.
    /// </summary>
    public sealed class CarOnTrack : MonoBehaviour
    {
        [SerializeField] private float radiusX = 24f;
        [SerializeField] private float radiusZ = 14f;
        [SerializeField] private float speed = 14f;
        [SerializeField] private Transform chaseCamera;
        [SerializeField] private float cameraDistance = 9f;
        [SerializeField] private float cameraHeight = 4.5f;

        private float angle;

        private void Update()
        {
            // Advance by arc length so the speed is roughly constant around the ellipse.
            var tangentLength = new Vector2(-radiusX * Mathf.Sin(angle), radiusZ * Mathf.Cos(angle)).magnitude;
            angle += speed * Time.deltaTime / Mathf.Max(0.001f, tangentLength);

            var position = new Vector3(radiusX * Mathf.Cos(angle), 0f, radiusZ * Mathf.Sin(angle));
            var forward = new Vector3(-radiusX * Mathf.Sin(angle), 0f, radiusZ * Mathf.Cos(angle)).normalized;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));

            if (chaseCamera != null)
            {
                var target = position - forward * cameraDistance + Vector3.up * cameraHeight;
                var t = 1f - Mathf.Exp(-4f * Time.deltaTime);
                chaseCamera.position = Vector3.Lerp(chaseCamera.position, target, t);
                chaseCamera.rotation = Quaternion.Slerp(chaseCamera.rotation, Quaternion.LookRotation(position + Vector3.up - chaseCamera.position), t);
            }
        }
    }
}
