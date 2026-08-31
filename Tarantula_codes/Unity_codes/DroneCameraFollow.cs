using UnityEngine;

public class DroneCameraFollow : MonoBehaviour
{
    [Header("Takip Edilecek Nesne")]
    [Tooltip("Buraya robotun ana gövdesini veya RobotRoot objesini ver.")]
    public Transform target;

    [Header("Kamera Konumu")]
    [Tooltip("Kameranýn robotun üstündeki yüksekliði")]
    public float height = 700f;

    [Tooltip("Kamera robotun arkasýnda ne kadar dursun")]
    public float distanceBack = 120f;

    [Tooltip("Kamera hareket yumuþaklýðý")]
    public float smoothSpeed = 5f;

    [Header("Bakýþ Ayarlarý")]
    [Tooltip("Kamera robotun ön tarafýna doðru baksýn diye ileri bakýþ mesafesi")]
    public float lookAheadDistance = 90f;

    [Tooltip("Ýstenen deðer: -391. Kamera aþaðý/öne doðru bakar.")]
    public float lookHeightOffset = -391f;

    [Header("Yön Düzeltme")]
    [Tooltip("Kamera ters yöne bakýyorsa 0 yap. Diðer durumda 180 yap.")]
    public float rotationOffsetY = 180f;

    [Tooltip("Kamera robotun yanlýþ tarafýnda duruyorsa bunu aç/kapat.")]
    public bool cameraOppositeSide = false;

    [Header("Takip Ayarý")]
    [Tooltip("Robot döndükçe kamera da yönünü takip etsin mi?")]
    public bool followTargetRotation = true;

    [Header("Robotu Ekranda Tutma")]
    [Tooltip("Ön bacaklarý görmek için false olmalý.")]
    public bool alwaysLookAtRobot = false;

    [Header("Debug")]
    public Vector3 desiredPosition;
    public Vector3 lookPoint;
    public float currentYaw;

    private Vector3 cameraVelocity;

    void LateUpdate()
    {
        if (target == null)
            return;

      

        UpdateYaw();

        Quaternion yawRotation = Quaternion.Euler(0f, currentYaw + rotationOffsetY, 0f);

        Vector3 forwardDirection = yawRotation * Vector3.forward;
        Vector3 backDirection = -forwardDirection;

        if (cameraOppositeSide)
            backDirection = -backDirection;

        
        desiredPosition =
            target.position +
            backDirection * distanceBack +
            Vector3.up * height;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref cameraVelocity,
            1f / Mathf.Max(0.01f, smoothSpeed)
        );

        if (alwaysLookAtRobot)
        {
            lookPoint = target.position + Vector3.up * lookHeightOffset;
        }
        else
        {
            
            lookPoint =
                target.position +
                forwardDirection * lookAheadDistance +
                Vector3.up * lookHeightOffset;
        }

        Vector3 lookDirection = lookPoint - transform.position;

        if (lookDirection.sqrMagnitude > 0.001f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                Time.deltaTime * smoothSpeed
            );
        }
    }

    void UpdateYaw()
    {
        if (followTargetRotation && target != null)
        {
            currentYaw = target.eulerAngles.y;
        }
        else
        {
            currentYaw = 0f;
        }
    }
}