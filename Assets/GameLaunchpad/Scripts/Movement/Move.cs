using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.coldcloudmedia
{
    [RequireComponent(typeof(Rigidbody))]
    public class Move : IMovement
    {
        [SerializeField]
        private Direction directionToMove;

        [SerializeField]
        private MovementSpeed movementSpeed;

        protected override void HandleMovement()
        {
            float calculatedForce = inputAxisValue * movementSpeed.GetSpeedValue();

            if (directionToMove == Direction.LEFT_RIGHT)
                force = transform.right * calculatedForce;
            if (directionToMove == Direction.UP_DOWN)
                force = transform.up * calculatedForce;
            if (directionToMove == Direction.FORWARD_BACK)
                force = transform.forward * calculatedForce;

            rigidbody.AddForce(force * Time.deltaTime);
        }
    }

}
