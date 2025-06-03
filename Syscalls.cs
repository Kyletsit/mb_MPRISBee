using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxSys
{
    public static class Syscalls
    {
        private const string DLL_NAME = "linux_syscalls.dll";

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int l_mkdir([MarshalAs(UnmanagedType.LPStr)] string pathname, uint mode);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int l_rmdir([MarshalAs(UnmanagedType.LPStr)] string pathname);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint l_getpid();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint l_getuid();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int l_close(int fd);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int l_socketcall(int call, IntPtr args);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int l_fcntl(uint fd, uint cmd, ulong arg);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int l_open([MarshalAs(UnmanagedType.LPStr)] string filename, int flags, int mode);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int l_write(uint fd, byte[] buf, uint count);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int l_write_errno(uint fd, byte[] buf, uint count);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int l_read(uint fd, byte[] buf, uint count);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int l_socket(int domain, int type, int protocol);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int l_connect(int sockfd, IntPtr addr, uint addrlen);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int l_connect_path(int sockfd, [MarshalAs(UnmanagedType.LPStr)] string path);
    }

    public class Socket
    {
        // Local communication domain
        public const int AF_UNIX = 1;

        // Byte stream type
        public const int SOCK_STREAM = 1;

        // File flags constants
        public const int O_RDONLY = 0;
        public const int O_WRONLY = 1;
        public const int O_RDWR = 2;
        public const int O_CREAT = 64;
        public const int O_TRUNC = 512;
        public const int O_APPEND = 1024;

        [StructLayout(LayoutKind.Sequential)]
        private struct SockAddrUn
        {
            public ushort sunFamily;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 108)]
            public byte[] sunPath;
        }

        private SockAddrUn socketAddress;
        public uint sockAddrSize;
        private int fileDescriptor;
        private string path;

        private readonly Queue<byte> readLeftoverBuffer;

        public Socket(string path)
        {
            try
            {
                Console.WriteLine($"MPRISBee D: Socket constructor start");
                CreateUnixSocketAddress(path);
                Console.WriteLine($"MPRISBee D: Socket constructor passed CreateUnixSocketAddress");
                this.path = path;
                OpenUnixSocket();
                Console.WriteLine($"MPRISBee D: Socket constructor passed OpenUnixSocket");
                ConnectUnixSocket();
                Console.WriteLine($"MPRISBee D: Socket constructor passed ConnectUnixSocket");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MPRISBee E: Socket object instance failed IN {ex}");
            }


            readLeftoverBuffer = new Queue<byte>();
            Console.WriteLine($"MPRISBee D: Socket constructor passed");
        }
        ~Socket()
        {
            CloseUnixSocket(fileDescriptor);
        }

        private void CreateUnixSocketAddress(string path)
        {
            socketAddress = new SockAddrUn();
            socketAddress.sunFamily = (ushort)AF_UNIX;
            socketAddress.sunPath = new byte[108];

            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            if (pathBytes.Length >= 108)
                throw new ArgumentException("Path too long for a Unix socket");

            Array.Copy(pathBytes, socketAddress.sunPath, pathBytes.Length);
            socketAddress.sunPath[pathBytes.Length] = 0;

            sockAddrSize = 2 + (uint)pathBytes.Length + 1; // ushort + string + \0
        }

        private void OpenUnixSocket()
        {
            fileDescriptor = Syscalls.l_socket(AF_UNIX, SOCK_STREAM, 0);
            if (fileDescriptor < 0)
            {
                throw new SystemException("Cannot open a new socket");
            }
        }

        // Helper method for Unix socket connection
        private void ConnectUnixSocket()
        {
            /*var res = Syscalls.l_connect_path(fileDescriptor, path);

            if (res < 0)
            {
                Console.WriteLine($"MPRISBee E: Errno: {res}");
                throw new IOException("MPRISBee E: Cannot connect to a socket");
            }*/

            IntPtr addrPtr = Marshal.AllocHGlobal(110);
            try
            {
                Marshal.StructureToPtr(socketAddress, addrPtr, false);
                Console.WriteLine($"MPRISBee D: ConnectUnixSocket fd: {fileDescriptor}, addPtr: {addrPtr}, size: {sockAddrSize}");
                var res = Syscalls.l_connect(fileDescriptor, addrPtr, sockAddrSize);
                if (res < 0)
                {
                    int errno = -res;
                    Console.WriteLine($"MPRISBee E: Errno: {errno}");
                    throw new IOException("MPRISBee E: Cannot connect to a socket");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(addrPtr);
            }
        }

        private void CloseUnixSocket(int fd)
        {
            if (Syscalls.l_close(fd) < 0)
            {
                throw new SystemException("Cannot close this socket");
            }
        }

        public void WriteStringNLTerminated(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text + '\n');
            int totalWritten = 0;

            while (totalWritten < bytes.Length)
            {
                int remaining = bytes.Length - totalWritten;
                
                byte[] slice = new byte[remaining];
                Buffer.BlockCopy(bytes, totalWritten, slice, 0, remaining);

                int written = Syscalls.l_write_errno(
                    (uint)fileDescriptor,
                    slice,
                    (uint)remaining
                );

                if (written < 0)
                {
                    int errno = -written;
                    Console.WriteLine($"MPRISBee E: Errno: {errno}");
                    throw new IOException($"Write failed. Written: {totalWritten}");
                }

                totalWritten += written;
            }
        }

        public string ReadString(uint maxLength)
        {
            byte[] buffer = new byte[maxLength];
            uint totalRead = 0;

            while (readLeftoverBuffer.Count > 0 && totalRead < maxLength)
            {
                byte b = readLeftoverBuffer.Dequeue();
                buffer[totalRead] = b;
                totalRead++;
            }

            while (totalRead < maxLength)
            {
                uint remaining = maxLength - totalRead;
                byte[] readBuffer = new byte[remaining];

                int bytesRead = Syscalls.l_read((uint)fileDescriptor, readBuffer, remaining);

                if (bytesRead < 0)
                {
                    throw new IOException($"Read failed. Total bytes read: {totalRead}");
                }

                if (bytesRead == 0)
                {
                    break;
                }

                Buffer.BlockCopy(readBuffer, 0, buffer, (int)totalRead, bytesRead);
                totalRead += (uint)bytesRead;
            }

            return Encoding.UTF8.GetString(buffer, 0, (int)totalRead);
        }

        public string ReadNLTerminatedString()
        {
            List<byte> result = new List<byte>();
            const int chunkSize = 256;

            var stopwatch = Stopwatch.StartNew();
            const int timeoutMillis = 500;

            bool foundNL = false;
            while (!foundNL)
            {
                while (readLeftoverBuffer.Count > 0)
                {
                    byte b = readLeftoverBuffer.Dequeue();
                    if (b == '\n')
                    {
                        return Encoding.UTF8.GetString(result.ToArray());
                    }
                    result.Add(b);
                }

                // Read from the socket
                byte[] chunk = new byte[chunkSize];
                int bytesRead = Syscalls.l_read((uint)fileDescriptor, chunk, (uint)chunkSize);

                if (bytesRead < 0)
                {
                    throw new IOException($"Read failed. Bytes collected: {result.Count}");
                }

                if (bytesRead == 0)
                {
                    // Possibly temporary lack of data — wait and retry

                    if (stopwatch.ElapsedMilliseconds < timeoutMillis)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    throw new TimeoutException($"Socket read timed out after {timeoutMillis}ms waiting for null terminator.");
                }

                // Process newly read data
                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = chunk[i];
                    if (b == '\n')
                    {
                        foundNL = true;
                        for (int j = i + 1; j < bytesRead; j++)
                        {
                            readLeftoverBuffer.Enqueue(chunk[j]);
                        }
                        break;
                    }
                    result.Add(b);
                }
            }

            return Encoding.UTF8.GetString(result.ToArray());
        }
    }
}
