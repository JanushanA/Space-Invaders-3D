using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    // Jake's orientation-relative movement and speed cap, shared with flight.
    // PlayerController alone writes the flight transform; ground mode is retained below.
    public static Vector3 ComputeFlightVelocity(Vector2 input, Vector3 forward, Vector3 right,
        float speed, float elevation, float verticalSpeed)
    {
        Vector3 direction = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
        Vector3 velocity = direction * Mathf.Max(0f, speed);
        float vertical = Mathf.Clamp(elevation, -1f, 1f) * Mathf.Max(0f, verticalSpeed);
        bool alreadyClimbing = Mathf.Abs(velocity.y) > 0.25f && Mathf.Abs(vertical) > 0.01f &&
                              Mathf.Sign(velocity.y) == Mathf.Sign(vertical);
        if (!alreadyClimbing)
        {
            velocity.y += vertical;
        }

        return velocity;
    }
    [Header("Movement")]
    public float moveSpeed;
    public float groundDrag;
    [Header("Ground Check")]
    public float playerHeight;
    public LayerMask whatISGround;
    bool grounded;
    public Transform orientation;
    float horizontalInput;
    float verticalInput;
    Vector3 moveDirection;
    Rigidbody rb;

    // Start is called before the first frame update
    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
    }

    private void Update()
    {
        grounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, whatISGround);
        MyInput();
        SpeedControl();
        if (grounded)
        {
            rb.drag = groundDrag;
        }
        else
        {
            rb.drag = 0;
        }
    }

    private void FixedUpdate()
    {
        MovePlayer();
    }

    // Update is called once per frame
    private void MyInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");
    }

    private void MovePlayer()
    {
        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;
        rb.AddForce(moveDirection.normalized * moveSpeed * 10f, ForceMode.Force);
    }

    private void SpeedControl()
    {
        Vector3 flatVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        if (flatVel.magnitude > moveSpeed)
        {
            Vector3 limitedVel = flatVel.normalized * moveSpeed;
            rb.velocity = new Vector3(limitedVel.x, rb.velocity.y, limitedVel.z);
        }
    }
}
