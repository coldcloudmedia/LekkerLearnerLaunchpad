using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.coldcloudmedia
{
    public class Rotate : IMovement
    {
        [SerializeField]
        private Vector3 rotationAxis;

        [SerializeField]
        private RotationSpeed rotationSpeed;

        protected override void HandleMovement()
        {
            transform.Rotate(rotationAxis * inputAxisValue * rotationSpeed.GetSpeedValue() * Time.deltaTime);
        }
    }
}
