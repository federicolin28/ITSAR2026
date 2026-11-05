using System;
using Unity.VisualScripting;
using UnityEditor.Experimental.GraphView;
using UnityEngine;  //namespace principale di Unity (MonoBe
using UnityEngine.Windows;
public class PlayerController : MonoBehaviour
{
    [SerializeField] CharacterController characterController;
    [SerializeField] Transform cameraTransform;
    private GameControls controls;


    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float sprintMultiplier = 1.5f;
    [SerializeField] float gravity = 10f;
    [SerializeField] float rotationSpeed = 720f;


    private void Awake() => controls = new GameControls();


    private void OnEnable() => controls.Player.Enable();


    private void OnDisable() => controls.Player.Disable();


    private void OnDestroy() => controls.Dispose();


    private void Update()
    {
        ApplyGravity();
        MoveCharacter();
        RotateToDirection();
    }


    void MoveCharacter()
    {
        Vector2 input = controls.Player.Move.ReadValue<Vector2>();
        Debug.Log(input);


        float speed = moveSpeed;
        if (controls.Player.Sprint.IsPressed())
            speed *= sprintMultiplier;


        //Vector3 moveVector = new Vector3(input.x /*x*/, input.y /*z*/ * speed;
        Vector3 moveVector = new Vector3(input.x, 0, input.y) * speed;
        characterController.Move(moveVector * Time.deltaTime);


    }


    void ApplyGravity()
    {
        float verticalVelocity = 0f;
        if (characterController.isGrounded)
        {
            verticalVelocity = -2f; //resta appoggiato
        }
        else
        {
            verticalVelocity = -gravity;
        }


        Vector3 velocity = Vector3.up * verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);
    }


    void RotateToDirection()
    {
        Vector2 directionRaw = controls.Player.Move.ReadValue<Vector2>();
        Vector3 direction = new Vector3(directionRaw.x, 0f, directionRaw.y);


        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);


            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }


    private Vector3 CameraRelative(Vector2 input)
    {
        if (cameraTransform == null)
            return new Vector3(input.x, 0f, input.y);


        //Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;
        forward.Normalize();


        Vector3 right = cameraTransform.right;
        right.y = 0f;
        right.Normalize();


        return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
    }
}