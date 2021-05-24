using UnityEngine;
using System.Collections;

namespace com.coldcloudmedia
{
    public enum MovementSpeed
    {
        EXTRA_SLOW, SLOW, MEDIUM, FAST, CRAZY_FAST, LUDICROUS_SPEED
    }

    static class MovementSpeedMethods
    {

        public static float GetSpeedValue(this MovementSpeed movementSpeed)
        {
            switch (movementSpeed)
            {
                case MovementSpeed.EXTRA_SLOW:
                    return 100;
                case MovementSpeed.SLOW:
                    return 500;
                case MovementSpeed.MEDIUM:
                    return 1000;
                case MovementSpeed.FAST:
                    return 2000;
                case MovementSpeed.CRAZY_FAST:
                    return 5000;
                case MovementSpeed.LUDICROUS_SPEED:
                    return 300000;
                default:
                    return 0;
            }
        }

    }
}
