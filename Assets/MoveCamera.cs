using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveCamera : MonoBehaviour
{
    public Transform cameraPostion;
    public bool followRotation;

    // Update is called once per frame
    void LateUpdate()
    {
        if (cameraPostion == null)
        {
            return;
        }

        transform.position = cameraPostion.position;
        if (followRotation)
        {
            transform.rotation = cameraPostion.rotation;
        }
    }
}
