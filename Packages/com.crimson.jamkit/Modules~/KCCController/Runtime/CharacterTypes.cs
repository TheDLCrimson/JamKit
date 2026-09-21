using UnityEngine;

namespace JamKit.KCC
{
    /// <summary>
    /// How the character was asked to change crouch state this frame.
    /// </summary>
    public enum CrouchInput
    {
        None, Toggle
    }

    /// <summary>
    /// The character's current locomotion stance.
    /// </summary>
    public enum Stance
    {
        Stand, Crouch, Slide
    }

    /// <summary>
    /// A snapshot of the character's motion state, taken once per character update.
    /// </summary>
    public struct CharacterState
    {
        public bool Grounded;
        public Stance Stance;
        public Vector3 Velocity;
        public Vector3 Acceleration;
    }

    /// <summary>
    /// One frame of character input, gathered by <see cref="Player"/> and handed to
    /// <see cref="PlayerCharacter.UpdateInput"/>.
    /// </summary>
    public struct CharacterInput
    {
        public Quaternion Rotation;
        public Vector2 Move;
        public bool Jump;
        public bool JumpSustain;
        public CrouchInput Crouch;
    }

    /// <summary>
    /// One frame of look input, gathered by <see cref="Player"/> and handed to
    /// <see cref="PlayerCamera.UpdateRotation"/>.
    /// </summary>
    public struct CameraInput
    {
        public Vector2 Look;
    }
}
