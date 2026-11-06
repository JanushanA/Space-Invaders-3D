using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FirstPersonCamera : MonoBehaviour
{
    public Transform player;
    public float mouseSensitivity = 2f;
    float cameraVerticalRotaion = 0f;

    bool lockedCursor = true;

    // Start is called before the first frame update
    void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }
    // Update is called once per frame
    void Update()
    {
        float inputX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float inputY = Input.GetAxis("Mouse Y") * mouseSensitivity;


        cameraVerticalRotaion -= inputY;
        cameraVerticalRotaion = Mathf.Clamp(cameraVerticalRotaion, -90f, 90f);
        transform.localEulerAngles = Vector3.right * cameraVerticalRotaion;
        player.Rotate(Vector3.up * inputX);
    }
}
