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
        #region Constants

        private const ushort HandlerId = 0x51dd;

        private const int FirstRepeatPeriod = 18;
        private const int RepeatPeriod = 6;

        private static readonly Vector3I MinOffset = new Vector3I(-50, -50, -50);
        private static readonly Vector3I MaxOffset = new Vector3I(+50, +50, +50);

        #endregion

        #region Keys

        private static readonly MyKeys[][] OffsetKeys =
        {
            new[] {MyKeys.S},
            new[] {MyKeys.W},
            new[] {MyKeys.D, MyKeys.Right},
            new[] {MyKeys.A, MyKeys.Left},
            new[] {MyKeys.C, MyKeys.Down},
            new[] {MyKeys.Space, MyKeys.Up},
        };

        private static readonly MyKeys[] RotationKeys =
        {
            MyKeys.Delete,
            MyKeys.PageDown,
            MyKeys.PageUp,
            MyKeys.Insert,
            MyKeys.Home,
            MyKeys.End,
        };

        #endregion

        #region Logic

        private static Aligner instance;

        private IMyProjector projector;
        private Vector3I offset;
        private Vector3I rotation;
        private MyKeys lastPressed;
        private int repeatCountdown;

        private bool Active => projector != null;

        public Aligner()
        {
            instance = this;

            Comms.PacketReceived += OnPacketReceived;
        }

        public void Dispose()
        {
            Comms.PacketReceived -= OnPacketReceived;

            Release();
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
                foreach (var offsetKey in OffsetKeys[directionIndex])
                {
                    if (!MyAPIGateway.Input.IsKeyPress(offsetKey))
                        continue;

                    var increment = GetIncrementByDirection(directionIndex);
                    offset = NormalizeProjectorOffset(offset + increment);
                    pressed = offsetKey;
                    break;
                }

                if (pressed != MyKeys.None)
                    break;

                var rotationKey = RotationKeys[directionIndex];
                if (MyAPIGateway.Input.IsKeyPress(rotationKey))
                {
                    var increment = GetIncrementByDirection(directionIndex);
                    rotation = NormalizeProjectorRotation(rotation + increment);
                    pressed = rotationKey;
                    break;
                }
            }

            if (projector.ProjectionOffset == offset &&
                projector.ProjectionRotation == rotation)
                return;

            projector.ProjectionOffset = offset;
            projector.ProjectionRotation = rotation;
            projector.UpdateOffsetAndRotation();

            if (pressed == MyKeys.None)
            {
                lastPressed = MyKeys.None;
                return;
            }

            repeatCountdown = pressed == lastPressed ? RepeatPeriod : FirstRepeatPeriod;
            lastPressed = pressed;

            SendOffsetAndRotationToServer();
        }

        // ReSharper disable once ParameterHidesMember
        private void Assign(IMyProjector projector)
        {
            this.projector = projector;
            offset = projector.ProjectionOffset;
            rotation = projector.ProjectionRotation;
        }

        // Client only
        private void Release()
        {
            projector = null;
        }

        private static Vector3I NormalizeProjectorOffset(Vector3I offset) => Vector3I.Max(MinOffset, Vector3I.Min(MaxOffset, offset));

        private static Vector3I NormalizeProjectorRotation(Vector3I r) => new Vector3I(NormalizeRotationValue(r.X), NormalizeRotationValue(r.Y), NormalizeRotationValue(r.Z));

        private static int NormalizeRotationValue(int v) => ((v + 1) & 3) - 1;

        private Vector3I GetIncrementByDirection(int directionIndex)
        {
            var direction = (Base6Directions.Direction) directionIndex;
            var directionVector = MyAPIGateway.Session.LocalHumanPlayer.Character.WorldMatrix.GetDirectionVector(direction);
            var closestProjectorDirection = projector.WorldMatrix.GetClosestDirection(directionVector);
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

        #endregion

        #region Networking

        [ProtoContract]
        private struct Packet
        {
            [ProtoMember(2)] public long projectorId;

            [ProtoMember(3)] public Vector3I offset;

            [ProtoMember(4)] public Vector3I rotation;
        }

        // Client only
        private void SendOffsetAndRotationToServer()
        {
            if (Comms.Role != Role.MultiplayerClient)
                return;

            Comms.SendToServer(HandlerId, new Packet
            {
                projectorId = projector.EntityId,
                offset = offset,
                rotation = rotation
            });
        }

        // Server only
        private static void OnPacketReceived(ushort handlerId, byte[] data, ulong fromSteamId, bool fromServer)
        {
            var packet = MyAPIGateway.Utilities.SerializeFromBinary<Packet>(data);
            var projector = MyAPIGateway.Entities.GetEntityById(packet.projectorId) as IMyProjector;
            if (projector == null)
                return;

            projector.ProjectionOffset = packet.offset;
            projector.ProjectionRotation = packet.rotation;
            projector.UpdateOffsetAndRotation();
        }

        #endregion
    }
}