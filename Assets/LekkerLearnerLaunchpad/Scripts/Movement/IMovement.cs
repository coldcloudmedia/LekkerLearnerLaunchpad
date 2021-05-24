using UnityEngine;

namespace com.coldcloudmedia
{

        [RequireComponent(typeof(Rigidbody))]
        public abstract class IMovement : MonoBehaviour
        {
            protected new Rigidbody rigidbody;

            [SerializeField]
            InputAxisLeftRight buttons;

            protected internal float inputAxisValue;

            internal Vector3 force = Vector3.zero;

            // Start is called before the first frame update
            void Start()
            {
                rigidbody = GetComponent<Rigidbody>();
            }

            // Update is called once per frame
            void Update()
            {
                inputAxisValue = Input.GetAxis(buttons.GetAxisString());
            }

            private void FixedUpdate()
            {
                HandleMovement();
            }

            protected abstract void HandleMovement();
    }
}