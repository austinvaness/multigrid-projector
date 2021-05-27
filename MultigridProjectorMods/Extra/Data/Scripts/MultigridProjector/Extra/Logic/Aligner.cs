using System;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Input;
using VRageMath;

// ReSharper disable once CheckNamespace
namespace MultigridProjector.Extra
{
    public class Aligner : IDisposable
    {
        private const int Magic = 0x51ddf2d4;

        private const int FirstRepeatPeriod = 18;
        private const int RepeatPeriod = 6;

        private static readonly Vector3I MinOffset = new Vector3I(-50, -50, -50);
        private static readonly Vector3I MaxOffset = new Vector3I(+50, +50, +50);

        private static readonly MyKeys[] offsetKeys =
        {
            MyKeys.S,
            MyKeys.W,
            MyKeys.D,
            MyKeys.A,
            MyKeys.C,
            MyKeys.Space,
        };

        private static readonly MyKeys[] rotationKeys =
        {
            MyKeys.Delete,
            MyKeys.PageDown,
            MyKeys.PageUp,
            MyKeys.Insert,
            MyKeys.Home,
            MyKeys.End,
        };

        private static Aligner instance;

        private IMyProjector controlledProjector;
        private Vector3I offset;
        private Vector3I rotation;
        private MyKeys lastPressed;
        private int repeatCountdown;

        private bool Active => controlledProjector != null;

        public Aligner()
        {
            instance = this;
            Comms.PacketReceived += OnPacketReceived;
        }

        public void Dispose()
        {
            Comms.PacketReceived -= OnPacketReceived;
        }

        // ReSharper disable once ParameterHidesMember
        private void Assign(IMyProjector projector)
        {
            if (!Comms.HasLocalPlayer)
                return;

            controlledProjector = projector;
            offset = projector.ProjectionOffset;
            rotation = projector.ProjectionRotation;
        }

        private void Release()
        {
            controlledProjector = null;
        }

        public void HandleInput()
        {
            if (!Active)
                return;

            if (MyAPIGateway.Gui.ChatEntryVisible ||
                MyAPIGateway.Gui.IsCursorVisible ||
                MyAPIGateway.Session.LocalHumanPlayer.Character.ControllerInfo.IsLocallyHumanControlled())
            {
                Release();
                return;
            }

            if (lastPressed != MyKeys.None && --repeatCountdown > 0)
            {
                if (MyAPIGateway.Input.IsKeyPress(lastPressed))
                    return;

                lastPressed = MyKeys.None;
                repeatCountdown = 0;
            }

            if (MyAPIGateway.Session?.LocalHumanPlayer?.Character == null)
                return;

            var pressed = MyKeys.None;
            for (var directionIndex = 0; directionIndex < 6; directionIndex++)
            {
                var key = offsetKeys[directionIndex];
                if (MyAPIGateway.Input.IsKeyPress(key))
                {
                    var increment = GetIncrementByDirection(directionIndex);
                    offset = NormalizeProjectorOffset(offset + increment);
                    pressed = key;
                    break;
                }

                key = rotationKeys[directionIndex];
                if (MyAPIGateway.Input.IsKeyPress(key))
                {
                    var increment = GetIncrementByDirection(directionIndex);
                    rotation = NormalizeProjectorRotation(rotation + increment);
                    pressed = key;
                    break;
                }
            }

            if (controlledProjector.ProjectionOffset == offset &&
                controlledProjector.ProjectionRotation == rotation)
                return;

            controlledProjector.ProjectionOffset = offset;
            controlledProjector.ProjectionRotation = rotation;

            if (pressed == MyKeys.None)
            {
                lastPressed = MyKeys.None;
                return;
            }

            repeatCountdown = pressed == lastPressed ? RepeatPeriod : FirstRepeatPeriod;
            lastPressed = pressed;
        }

        private static Vector3I NormalizeProjectorOffset(Vector3I offset) => Vector3I.Max(MinOffset, Vector3I.Min(MaxOffset, offset));

        private static Vector3I NormalizeProjectorRotation(Vector3I r) => new Vector3I(NormalizeRotationValue(r.X), NormalizeRotationValue(r.Y), NormalizeRotationValue(r.Z));

        private static int NormalizeRotationValue(int v) => ((v + 1) & 3) - 1;

        private Vector3I GetIncrementByDirection(int directionIndex)
        {
            var direction = (Base6Directions.Direction) directionIndex;
            var directionVector = MyAPIGateway.Session.LocalHumanPlayer.Character.WorldMatrix.GetDirectionVector(direction);
            var closestProjectorDirection = controlledProjector.WorldMatrix.GetClosestDirection(directionVector);
            return Base6Directions.IntDirections[(int) closestProjectorDirection];
        }

        public static bool Getter(IMyTerminalBlock block)
        {
            return instance?.Active ?? false;
        }

        public static void Setter(IMyTerminalBlock block, bool enabled)
        {
            var projector = block as IMyProjector;
            if (projector == null)
                return;

            if (enabled)
                instance?.Assign(projector);
            else
                instance?.Release();
        }

        public static void Toggle(IMyTerminalBlock block)
        {
            Setter(block, !Getter(block));
        }

        [ProtoContract]
        private struct Payload
        {
            [ProtoMember(1)]
            public long projectorId;

            [ProtoMember(1)]
            public bool assign;
        }

        private void OnPacketReceived(Packet packet)
        {
            if (packet.magic != Magic)
                return;

            var payload = MyAPIGateway.Utilities.SerializeFromBinary<Payload>(packet.payload);
            var projector = MyAPIGateway.Entities.GetEntityById(payload.projectorId) as IMyProjector;
            if (projector == null)
                return;

            if (payload.assign)
                Assign(projector);
            else
                Release();
        }
    }
}