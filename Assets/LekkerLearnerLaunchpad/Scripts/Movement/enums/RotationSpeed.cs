using UnityEngine;
using System.Collections;

namespace com.coldcloudmedia
{
    public enum RotationSpeed
    {
        EXTRA_SLOW, SLOW, MEDIUM, FAST, FASTEST
    }

    static class RotationSpeedMethods
    {

        public static float GetSpeedValue(this RotationSpeed movementSpeed)
        {
            switch (movementSpeed)
            {
                case RotationSpeed.EXTRA_SLOW:
                    return 5;
                case RotationSpeed.SLOW:
                    return 20;
                case RotationSpeed.MEDIUM:
                    return 40;
                case RotationSpeed.FAST:
                    return 200;
                case RotationSpeed.FASTEST:
                    return 350;
                default:
                    return 0;
            }
        }

    }
}
