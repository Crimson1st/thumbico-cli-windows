﻿//----------------------------------------------------------------------------------------------------
//
// <copyright file="ShellThumbnail.cs" company="Aurelitec">
// Copyright (c) Aurelitec (https://www.aurelitec.com)
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
//
// Description: Provides a method to return an icon or thumbnail for a Shell item.
//
//----------------------------------------------------------------------------------------------------

namespace ThumbicoCLI
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Interop;
    using System.Windows.Media.Imaging;

    /// <summary>
    /// Flag options for generating the thumbnail.
    /// </summary>
    [Flags]
    public enum ThumbnailFlags
    {
        /// <summary>
        /// Shrink the bitmap as necessary to fit, preserving its aspect ratio.
        /// </summary>
        ResizeToFit = 0x00000000,

        /// <summary>
        /// Passed by callers if they want to stretch the returned image themselves.
        /// </summary>
        BiggerSizeOk = 0x00000001,

        /// <summary>
        /// Return the item only if it is already in memory.
        /// </summary>
        MemoryOnly = 0x00000002,

        /// <summary>
        /// Return only the icon, never the thumbnail.
        /// </summary>
        IconOnly = 0x00000004,

        /// <summary>
        /// Return only the thumbnail, never the icon. Note that not all items have thumbnails,
        /// so this flag will cause the method to fail in these cases.
        /// </summary>
        ThumbnailOnly = 0x00000008,

        /// <summary>
        /// Allows access to the disk, but only to retrieve a cached item.
        /// </summary>
        InCacheOnly = 0x00000010,

        /// <summary>
        /// (Introduced in Windows 8) If necessary, crop the bitmap to a square.
        /// </summary>
        CropToSquare = 0x00000020,

        /// <summary>
        /// (Introduced in Windows 8) Stretch and crop the bitmap to a 0.7 aspect ratio.
        /// </summary>
        WideThumbnails = 0x00000040,

        /// <summary>
        /// (Introduced in Windows 8) If returning an icon, paint a background using the
        /// associated app's registered background color.
        /// </summary>
        IconBackground = 0x00000080,

        /// <summary>
        /// (Introduced in Windows 8) If necessary, stretch the bitmap so that the height
        /// and width fit the given size.
        /// </summary>
        ScaleUp = 0x00000100,
    }

    /// <summary>
    /// Provides a method to return an icon or thumbnail for a Shell item.
    /// </summary>
    public class ShellThumbnail
    {
        // Based on https://stackoverflow.com/questions/21751747/extract-thumbnail-for-any-file-in-windows

        private const string IShellItem2Guid = "7E9FB0D3-919F-4307-AB2E-9B1860310C93";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(IntPtr hObject);

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        internal interface IShellItem
        {
            void BindToHandler(IntPtr pbc,
                [MarshalAs(UnmanagedType.LPStruct)]Guid bhid,
                [MarshalAs(UnmanagedType.LPStruct)]Guid riid,
                out IntPtr ppv);

            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        };

        internal enum SIGDN : uint
        {
            NORMALDISPLAY = 0,
            PARENTRELATIVEPARSING = 0x80018001,
            PARENTRELATIVEFORADDRESSBAR = 0x8001c001,
            DESKTOPABSOLUTEPARSING = 0x80028000,
            PARENTRELATIVEEDITING = 0x80031001,
            DESKTOPABSOLUTEEDITING = 0x8004c000,
            FILESYSPATH = 0x80058000,
            URL = 0x80068000
        }

        internal enum HResult
        {
            Ok = 0x0000,
            False = 0x0001,
            InvalidArguments = unchecked((int)0x80070057),
            OutOfMemory = unchecked((int)0x8007000E),
            NoInterface = unchecked((int)0x80004002),
            Fail = unchecked((int)0x80004005),
            ExtractionFailed = unchecked((int)0x8004B200),
            ElementNotFound = unchecked((int)0x80070490),
            TypeElementNotFound = unchecked((int)0x8002802B),
            NoObject = unchecked((int)0x800401E5),
            Win32ErrorCanceled = 1223,
            Canceled = unchecked((int)0x800704C7),
            ResourceInUse = unchecked((int)0x800700AA),
            AccessDenied = unchecked((int)0x80030005)
        }

        [ComImportAttribute()]
        [GuidAttribute("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItemImageFactory
        {
            [PreserveSig]
            HResult GetImage(
            [In, MarshalAs(UnmanagedType.Struct)] NativeSize size,
            [In] ThumbnailFlags flags,
            [Out] out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeSize
        {
            private int width;
            private int height;

            public int Width { set => width = value; }
            public int Height { set => height = value; }
        };


        public static BitmapSource GetThumbnail(string fileName, int width, int height, ThumbnailFlags flags)
        {
            IntPtr hBitmap = GetHBitmap(Path.GetFullPath(fileName), width, height, flags);

            try
            {

                return Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                // delete HBitmap to avoid memory leaks
                DeleteObject(hBitmap);
            }
        }

        private static IntPtr GetHBitmap(string fileName, int width, int height, ThumbnailFlags flags)
        {
            IShellItem nativeShellItem;
            Guid shellItem2Guid = new Guid(IShellItem2Guid);
            int retCode = SHCreateItemFromParsingName(fileName, IntPtr.Zero, ref shellItem2Guid, out nativeShellItem);

            if (retCode != 0)
                throw Marshal.GetExceptionForHR(retCode);

            NativeSize nativeSize = new NativeSize
            {
                Width = width,
                Height = height
            };

            IntPtr hBitmap;
            HResult hr = ((IShellItemImageFactory)nativeShellItem).GetImage(nativeSize, flags, out hBitmap);

            // if extracting image thumbnail and failed, extract shell icon
            //if (flags == ThumbnailFlags.ThumbnailOnly && hr == HResult.ExtractionFailed)
            //{
            //    hr = ((IShellItemImageFactory)nativeShellItem).GetImage(nativeSize, ThumbnailFlags.IconOnly, out hBitmap);
            //}

            Marshal.ReleaseComObject(nativeShellItem);

            if (hr == HResult.Ok) return hBitmap;

            throw new COMException($"Error while extracting thumbnail for {fileName}", Marshal.GetExceptionForHR((int)hr));
        }
    }
}