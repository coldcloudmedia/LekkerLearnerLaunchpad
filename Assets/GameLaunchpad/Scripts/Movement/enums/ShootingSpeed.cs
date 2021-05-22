using UnityEngine;
using System.Collections;

namespace com.coldcloudmedia
{
    public enum ShootingSpeed
    {
        EXTRA_SLOW, SLOW, MEDIUM, FAST, FASTEST
    }

    static class ShootingSpeedMethods
    {

        public static float GetSpeedValue(this ShootingSpeed movementSpeed)
        {
            switch (movementSpeed)
            {
                case ShootingSpeed.EXTRA_SLOW:
                    return 5000;
                case ShootingSpeed.SLOW:
                    return 10000;
                case ShootingSpeed.MEDIUM:
                    return 20000;
                case ShootingSpeed.FAST:
                    return 40000;
                case ShootingSpeed.FASTEST:
                    return 80000;
                default:
                    return 0;
            }
        }

    }
}
