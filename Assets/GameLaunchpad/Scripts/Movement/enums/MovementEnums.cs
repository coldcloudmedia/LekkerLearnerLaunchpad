using UnityEngine;
using System.Collections;

namespace com.coldcloudmedia
{
    public enum Direction
    {
        LEFT_RIGHT, UP_DOWN, FORWARD_BACK
    }

    public enum InputAxisLeftRight
    {
        LeftAndRightArrows, UpAndDownArrows, ADButtons, WSButtons
    }

    static class InputAxisMethods
    {

        public static string GetAxisString(this InputAxisLeftRight s1)
        {
            switch (s1)
            {
                case InputAxisLeftRight.LeftAndRightArrows:
                    return "Horizontal";
                case InputAxisLeftRight.ADButtons:
                    return "Horizontal";
                case InputAxisLeftRight.UpAndDownArrows:
                    return "Vertical";
                case InputAxisLeftRight.WSButtons:
                    return "Vertical";
                default:
                    return "";
            }
        }

    }
}
