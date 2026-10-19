using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private float followSpeed = 0.1f;
    [SerializeField] private float zoomSize = 25.5f;
    [Tooltip("Color behind everything (wherever there's no sprite or tile) while the game plays.")]
    [SerializeField] private Color backgroundColor = new Color(0.12f, 0.12f, 0.13f); // dark gray
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Camera cam = GetComponent<Camera>();
        cam.orthographicSize = zoomSize;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = backgroundColor;
    }

    // LateUpdate is called once per frame, after all Update calls
    void LateUpdate()
    {
        transform.position = Vector3.Lerp(transform.position, new Vector3(PlayerController.Instance.transform.position.x, PlayerController.Instance.transform.position.y, transform.position.z), 1f - Mathf.Pow(1f - followSpeed, Time.deltaTime * 60f));
    }
}
