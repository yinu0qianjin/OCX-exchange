using UnityEngine;

namespace Zhouxiangyang
{
    public class Billboard : MonoBehaviour
    {
        public Camera targetCamera;
        public bool lockRotation = true;

        void LateUpdate()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;
            if (lockRotation)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - targetCamera.transform.position, Vector3.up);
            }
        }
    }
}
