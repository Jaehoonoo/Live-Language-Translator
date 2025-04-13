using UnityEngine;

public class FollowCamera : MonoBehaviour
{
  public Transform cameraTransform;

  public float distance = 2f; // Distance in front of the camera
  public Vector3 offset = Vector3.zero;

  void Update()
  {
    if (cameraTransform != null)
    {
      // Position the Canvas in front of the camera
      transform.position = cameraTransform.position + cameraTransform.forward * distance + offset;
      // Ensure the Canvas faces the camera
      transform.LookAt(cameraTransform);
      transform.Rotate(0, 180f, 0);
    }
  }
}
