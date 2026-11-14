using Midview.Protocol;
using Xunit;

// "Travar o toque do aluno": message 107, one byte.
public class StudentTouchLockTests
{
    [Fact]
    public void MessageType_IsTheWireNumber() => Assert.Equal(107, (int)MessageType.LockStudentTouch);

    [Fact]
    public void Locked_is_one_and_free_is_zero()
    {
        Assert.Equal(new byte[] { 1 }, StudentTouchLock.Encode(true));
        Assert.Equal(new byte[] { 0 }, StudentTouchLock.Encode(false));
        Assert.True(StudentTouchLock.IsLocked(StudentTouchLock.Encode(true)));
        Assert.False(StudentTouchLock.IsLocked(StudentTouchLock.Encode(false)));
    }

    // Anything else frees the touch: never a student left locked by nonsense.
    [Fact]
    public void Anything_else_is_free()
    {
        Assert.False(StudentTouchLock.IsLocked(System.Array.Empty<byte>()));
        Assert.False(StudentTouchLock.IsLocked(new byte[] { 1, 1 }));
        Assert.False(StudentTouchLock.IsLocked(new byte[] { 2 }));
    }
}
