using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace iPiMocap
{
    public class MocapStudioClient : MonoBehaviour
    {
        private static readonly byte[] SIGNATURE = Encoding.ASCII.GetBytes("iPiMocap");
        private const int PACKET_KIND_POSE = 0;
        private const int PACKET_KIND_NAMES = 1;

        private readonly ConcurrentQueue<Pose> poses = new ConcurrentQueue<Pose>();
        private string[] rotationNames = Array.Empty<string>();
        private Thread receivingThread;
        private ManualResetEvent stopReceiving;

        public int Port = 31455;

        public Pose FetchNextPose()
            => poses.TryDequeue(out var pose) ? pose : null;

        public Pose FetchLatestPose()
        {
            Pose result = null;
            Pose fetched;
            while ((fetched = FetchNextPose()) != null)
            {
                result = fetched;
            }

            return result;
        }

        // Start is called before the first frame update
        private void Start()
        {
            receivingThread = new Thread(ReceivePackets)
            {
                Name = nameof(ReceivePackets),
                IsBackground = true
            };
            stopReceiving = new ManualResetEvent(false);
            receivingThread.Start();
        }

        private void OnDestroy()
        {
            try
            {
                stopReceiving.Set();
                receivingThread.Join();
            }
            catch (ObjectDisposedException) { }
            catch (ThreadInterruptedException) { }
        }

        private void ReceivePackets()
        {
            using (stopReceiving)
            using (var udpClient = new UdpClient(Port))
            {
                while (!stopReceiving.WaitOne(0))
                {
                    var result = udpClient.BeginReceive(null, null);
                    var awaited = WaitHandle.WaitAny(new[] { result.AsyncWaitHandle, stopReceiving });

                    if (awaited == 0)
                    {
                        var endPoint = new IPEndPoint(IPAddress.Any, Port);
                        byte[] packet = null;
                        try
                        {
                            packet = udpClient.EndReceive(result, ref endPoint);
                        }
                        catch (SocketException) { }
                        catch (ObjectDisposedException) { }

                        if (packet != null)
                        {
                            HandlePacket(packet);
                        }
                    }
                }
            }
        }

        private void HandlePacket(byte[] packet)
        {
            var kind = GetPacketKind(packet);
            switch (kind)
            {
                case PACKET_KIND_POSE:
                    var pose = GetPoseFromPacket(packet);
                    if (pose != null)
                    {
                        poses.Enqueue(pose);
                    }
                    break;

                case PACKET_KIND_NAMES:
                    var names = GetRotationNamesFromPacket(packet);
                    if (names != null)
                    {
                        rotationNames = names;
                    }
                    break;
            }
        }

        private static int? GetPacketKind(byte[] packet)
        {
            if (!packet.Take(SIGNATURE.Length).SequenceEqual(SIGNATURE))
            {
                // Packet doesn't start with the signature => discard
                return null;
            }

            if (packet.Length < SIGNATURE.Length + sizeof(int))
            {
                // Packet doesn't contain kind => discard
                return null;
            }

            return BitConverter.ToInt32(packet, SIGNATURE.Length);
        }

        private Pose GetPoseFromPacket(byte[] packet)
        {
            using (var r = CreateReaderForPacket(packet))
            {
                try
                {
                    var pose = new Pose();

                    // By convention, root joint is the first in transmitted rotation names
                    pose.RootName = rotationNames.FirstOrDefault();

                    // Negate X coordinate to convert position from right-handed
                    pose.RootPosition.x = -r.ReadSingle();
                    pose.RootPosition.y = r.ReadSingle();
                    pose.RootPosition.z = r.ReadSingle();

                    var rotationCount = r.ReadInt32();
                    if (rotationCount == rotationNames.Length)
                    {
                        for (var i = 0; i < rotationCount; i++)
                        {
                            // Negate Y and Z components to convert rotation from right-handed
                            // (opposite direction of X axis)
                            Quaternion rotation;
                            rotation.x = r.ReadSingle();
                            rotation.y = -r.ReadSingle();
                            rotation.z = -r.ReadSingle();
                            rotation.w = r.ReadSingle();
                            pose.Rotations.Add(rotationNames[i], rotation);
                        }
                    }

                    return pose;
                }
                catch
                {
                    // Invalid format of the packet
                    return null;
                }
            }
        }

        private static string[] GetRotationNamesFromPacket(byte[] packet)
        {
            using (var r = CreateReaderForPacket(packet))
            {
                try
                {
                    var count = r.ReadInt32();
                    var result = new string[count];
                    for (var i = 0; i < count; i++)
                    {
                        result[i] = r.ReadString();
                    }
                    return result;
                }
                catch
                {
                    // Invalid format of the packet
                    return null;
                }
            }
        }

        private static BinaryReader CreateReaderForPacket(byte[] packet)
        {
            var ms = new MemoryStream(packet);
            // Skip signature and kind
            ms.Position = SIGNATURE.Length + sizeof(int);
            return new BinaryReader(ms);
        }
    }
}