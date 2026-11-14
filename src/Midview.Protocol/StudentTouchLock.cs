namespace Midview.Protocol;

// Payload of MessageType.LockStudentTouch: one byte, 1 locks the student's
// touch while the teacher controls a phone or tablet, 0 frees it.
public static class StudentTouchLock
{
    public static byte[] Encode(bool locked) => new[] { locked ? (byte)1 : (byte)0 };

    // Anything but a single 1 frees the touch.
    public static bool IsLocked(byte[] payload) => payload.Length == 1 && payload[0] == 1;
}
