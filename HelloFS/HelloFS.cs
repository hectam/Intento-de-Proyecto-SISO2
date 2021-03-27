//
// HelloFS.cs
//
// Authors:
//   Jonathan Pryor (jonpryor@vt.edu)
//
// (C) 2006 Jonathan Pryor
//
// Mono.Fuse example program
//

//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Mono.Fuse;
using Mono.Unix.Native;


namespace Mono.Fuse.Samples {

	struct SUPERBLOCK
	{
		
		public char[] title;
		public int BITMAPSIZE;
		public int partitionSize;
		public int nextBlock;

	}

	struct BLOCK
	{
		public char[] content;
		public int nextBlock;
	}

	struct B1
	{
		public BLOCK greatConteiner;
	}

	struct B2
	{
		public B1[] superConteiner;
	}

	class HelloFS : FileSystem {
		// private string basedir;
		const int BLOCK_SIZE = 4096;
		int[] BITMAP = new int[4096];

		SUPERBLOCK mango = new SUPERBLOCK();
		//LOS BLOQUES DE PRIMER NIVEL ESTAN MALOS
		B1[] directBlocks;
		B2[] secondLevel;
		
		static readonly byte[] hello_str = Encoding.UTF8.GetBytes ("Hello World!\n");
		const string hello_path = "/hello";
		const string data_path  = "/data";
		const string data_im_path  = "/data.im";

		const int data_size = 100000000;

		byte[] data_im_str;
		bool have_data_im = false;
		object data_im_str_lock = new object ();
		Dictionary<string, byte[]> hello_attrs = new Dictionary<string, byte[]>();
		
		public HelloFS ()
		{
			Trace.WriteLine ("(HelloFS creating)");
			hello_attrs ["foo"] = Encoding.UTF8.GetBytes ("bar");
		}

		public void initBitmap(){

			Array.Clear(BITMAP, 0, BITMAP.Length);

			directBlocks = new B1[64];

			secondLevel = new B2[63];
			
			int blockC = 1;

			for(int i = 0; i < 64; i++){

				directBlocks[i].greatConteiner.content = new char[BLOCK_SIZE];
			
			    directBlocks[i].greatConteiner.nextBlock = blockC+1;

				blockC += 1;
				
			}

			for(int x =0; x< 63;x++){

				secondLevel[x].superConteiner = new B1[64];

				for(int y = 0; y < 64;y++){
					secondLevel[x].superConteiner[y].greatConteiner.content = new char[BLOCK_SIZE];
					secondLevel[x].superConteiner[y].greatConteiner.nextBlock = blockC + 1;
					blockC += 1;
				}
			}

			mango.partitionSize = new int();
			mango.title = new char[256];
			mango.nextBlock = new int();

			mango.partitionSize = BLOCK_SIZE * blockC;
			string tester = "Test";
			mango.title = tester.ToCharArray();
			mango.nextBlock = 1;
			mango.BITMAPSIZE = blockC -1;


		}

		protected override Errno OnGetPathStatus (string path, out Stat stbuf)
		{
			Trace.WriteLine ("(OnGetPathStatus {0})", path);

		

			// int r = Syscall.lstat (basedir+path, out stbuf);
			// if (r == -1)
			// 	return Stdlib.GetLastError ();
			
		


			stbuf = new Stat ();
			switch (path) {
				case "/":
					stbuf.st_mode = FilePermissions.S_IFDIR | 
						NativeConvert.FromOctalPermissionString ("0755");
					stbuf.st_nlink = 2;
					return 0;
				case hello_path:
				case data_path:
				case data_im_path:
					stbuf.st_mode = FilePermissions.S_IFREG |
						NativeConvert.FromOctalPermissionString ("0444");
					stbuf.st_nlink = 1;
					int size = 0;
					switch (path) {
						case hello_path:   size = hello_str.Length; break;
						case data_path:
						case data_im_path: size = data_size; break;
					}
					stbuf.st_size = size;
					return 0;
				default:
					return Errno.ENOENT;
			}
		}

		protected override Errno OnReadDirectory (string path, OpenedPathInfo fi,
				out IEnumerable<DirectoryEntry> paths)
		{
			Trace.WriteLine ("(OnReadDirectory {0})", path);
			paths = null;
			if (path != "/")
				return Errno.ENOENT;

			paths = GetEntries ();
			return 0;
		}

		private IEnumerable<DirectoryEntry> GetEntries ()
		{
			yield return new DirectoryEntry (".");
			yield return new DirectoryEntry ("..");
			yield return new DirectoryEntry ("hello");
			yield return new DirectoryEntry ("data");
			if (have_data_im)
				yield return new DirectoryEntry ("data.im");
		}

		protected override Errno OnOpenHandle (string path, OpenedPathInfo fi)
		{
			Trace.WriteLine (string.Format ("(OnOpen {0} Flags={1})", path, fi.OpenFlags));
			if (path != hello_path && path != data_path && path != data_im_path)
				return Errno.ENOENT;
			if (path == data_im_path && !have_data_im)
				return Errno.ENOENT;
			if (fi.OpenAccess != OpenFlags.O_RDONLY)
				return Errno.EACCES;
			return 0;
		}

		protected override Errno OnReadHandle (string path, OpenedPathInfo fi, byte[] buf, long offset, out int bytesWritten)
		{
			Trace.WriteLine ("(OnRead {0})", path);
			bytesWritten = 0;
			int size = buf.Length;
			if (path == data_im_path)
				FillData ();
			if (path == hello_path || path == data_im_path) {
				byte[] source = path == hello_path ? hello_str : data_im_str;
				if (offset < (long) source.Length) {
					if (offset + (long) size > (long) source.Length)
						size = (int) ((long) source.Length - offset);
					Buffer.BlockCopy (source, (int) offset, buf, 0, size);
				}
				else
					size = 0;
			}
			else if (path == data_path) {
				int max = System.Math.Min ((int) data_size, (int) (offset + buf.Length));
				for (int i = 0, j = (int) offset; j < max; ++i, ++j) {
					if ((j % 27) == 0)
						buf [i] = (byte) '\n';
					else
						buf [i] = (byte) ((j % 26) + 'a');
				}
			}
			else
				return Errno.ENOENT;

			bytesWritten = size;
			return 0;
		}

		protected override Errno OnGetPathExtendedAttribute (string path, string name, byte[] value, out int bytesWritten)
		{
			Trace.WriteLine ("(OnGetPathExtendedAttribute {0})", path);
			bytesWritten = 0;
			if (path != hello_path) {
				return 0;
			}
			byte[] _value;
			lock (hello_attrs) {
				if (!hello_attrs.ContainsKey (name))
					return 0;
				_value = hello_attrs [name];
			}
			if (value.Length < _value.Length) {
				return Errno.ERANGE;
			}
			Array.Copy (_value, value, _value.Length);
			bytesWritten = _value.Length;
			return 0;
		}

		protected override Errno OnSetPathExtendedAttribute (string path, string name, byte[] value, XattrFlags flags)
		{
			Trace.WriteLine ("(OnSetPathExtendedAttribute {0})", path);
			if (path != hello_path) {
				return Errno.ENOSPC;
			}
			lock (hello_attrs) {
				hello_attrs [name] = value;
			}
			return 0;
		}

		protected override Errno OnRemovePathExtendedAttribute (string path, string name)
		{
			Trace.WriteLine ("(OnRemovePathExtendedAttribute {0})", path);
			if (path != hello_path)
				return Errno.ENODATA;
			lock (hello_attrs) {
				if (!hello_attrs.ContainsKey (name))
					return Errno.ENODATA;
				hello_attrs.Remove (name);
			}
			return 0;
		}

		protected override Errno OnListPathExtendedAttributes (string path, out string[] names)
		{
			Trace.WriteLine ("(OnListPathExtendedAttributes {0})", path);
			if (path != hello_path) {
				names = new string[]{};
				return 0;
			}
			List<string> _names = new List<string> ();
			lock (hello_attrs) {
				_names.AddRange (hello_attrs.Keys);
			}
			names = _names.ToArray ();
			return 0;
		}



		private bool ParseArguments (string[] args)
		{
			for (int i = 0; i < args.Length; ++i) {
				switch (args [i]) {
					case "--data.im-in-memory":
						have_data_im = true;
						break;
					case "-h":
					case "--help":
						Console.Error.WriteLine ("usage: hellofs [options] mountpoint");
						FileSystem.ShowFuseHelp ("hellofs");
						Console.Error.WriteLine ("hellofs options:");
						Console.Error.WriteLine ("    --data.im-in-memory    Add data.im file");
						return false;
					default:
						base.MountPoint = args [i];
						break;
				}
			}
			return true;
		}

		

		// protected override Errno OnAccessPath (string path, AccessModes mask)
		// {
		// 	int r = Syscall.access (basedir+path, mask);
		// 	if (r == -1)
		// 		return Stdlib.GetLastError ();
		// 	return 0;
		// }

		// protected override Errno OnReadSymbolicLink (string path, out string target)
		// {
		// 	target = null;
		// 	StringBuilder buf = new StringBuilder (256);
		// 	do {
		// 		int r = Syscall.readlink (basedir+path, buf);
		// 		if (r < 0) {
		// 			return Stdlib.GetLastError ();
		// 		}
		// 		else if (r == buf.Capacity) {
		// 			buf.Capacity *= 2;
		// 		}
		// 		else {
		// 			target = buf.ToString (0, r);
		// 			return 0;
		// 		}
		// 	} while (true);
		// }

		

		// protected override Errno OnCreateSpecialFile (string path, FilePermissions mode, ulong rdev)
		// {
		// 	int r;

		// 	// On Linux, this could just be `mknod(basedir+path, mode, rdev)' but this is
		// 	// more portable.
		// 	if ((mode & FilePermissions.S_IFMT) == FilePermissions.S_IFREG) {
		// 		r = Syscall.open (basedir+path, OpenFlags.O_CREAT | OpenFlags.O_EXCL |
		// 				OpenFlags.O_WRONLY, mode);
		// 		if (r >= 0)
		// 			r = Syscall.close (r);
		// 	}
		// 	else if ((mode & FilePermissions.S_IFMT) == FilePermissions.S_IFIFO) {
		// 		r = Syscall.mkfifo (basedir+path, mode);
		// 	}
		// 	else {
		// 		r = Syscall.mknod (basedir+path, mode, rdev);
		// 	}

		// 	if (r == -1)
		// 		return Stdlib.GetLastError ();

		// 	return 0;
		// }

		protected override Errno OnCreateDirectory (string path, FilePermissions mode)
		{
			

			int r = Syscall.mkdir (path, mode);
			if (r == -1)
				return Stdlib.GetLastError ();
			return 0;
		}

		// protected override Errno OnRemoveFile (string path)
		// {
		// 	int r = Syscall.unlink (basedir+path);
		// 	if (r == -1)
		// 		return Stdlib.GetLastError ();
		// 	return 0;
		// }

		protected override Errno OnRemoveDirectory (string path)
		{
			int r = Syscall.rmdir (path);
			if (r == -1)
				return Stdlib.GetLastError ();
			return 0;
		}

		// protected override Errno OnCreateSymbolicLink (string from, string to)
		// {
		// 	int r = Syscall.symlink (from, basedir+to);
		// 	if (r == -1)
		// 		return Stdlib.GetLastError ();
		// 	return 0;
		// }

		// protected override Errno OnRenamePath (string from, string to)
		// {
		// 	int r = Syscall.rename (basedir+from, basedir+to);
		// 	if (r == -1)
		// 		return Stdlib.GetLastError ();
		// 	return 0;
		// }

		// protected override Errno OnCreateHardLink (string from, string to)
		// {
		// 	int r = Syscall.link (basedir+from, basedir+to);
		// 	if (r == -1)
		// 		return Stdlib.GetLastError ();
		// 	return 0;
		// }

		// protected override Errno OnChangePathPermissions (string path, FilePermissions mode)
		// {
		// 	int r = Syscall.chmod (basedir+path, mode);
		// 	if (r == -1)
		// 		return Stdlib.GetLastError ();
		// 	return 0;
		// }

		// protected override Errno OnChangePathOwner (string path, long uid, long gid)
		// {
		// 	int r = Syscall.lchown (basedir+path, (uint) uid, (uint) gid);
		// 	if (r == -1)
		// 		return Stdlib.GetLastError ();
		// 	return 0;
		// }

		// protected override Errno OnTruncateFile (string path, long size)
		// {
		// 	int r = Syscall.truncate (basedir+path, size);
		// 	if (r == -1)
		// 		return Stdlib.GetLastError ();
		// 	return 0;
		// }

		// protected override Errno OnChangePathTimes (string path, ref Utimbuf buf)
		// {
		// 	int r = Syscall.utime (basedir+path, ref buf);
		// 	if (r == -1)
		// 		return Stdlib.GetLastError ();
		// 	return 0;
		// }

		
		private delegate int FdCb (int fd);
		private static Errno ProcessFile (string path, OpenFlags flags, FdCb cb)
		{
			int fd = Syscall.open (path, flags);
			if (fd == -1)
				return Stdlib.GetLastError ();
			int r = cb (fd);
			Errno res = 0;
			if (r == -1)
				res = Stdlib.GetLastError ();
			Syscall.close (fd);
			return res;
		}

		// protected override unsafe Errno OnReadHandle (string path, OpenedPathInfo info, byte[] buf, 
		// 		long offset, out int bytesRead)
		// {
		// 	int br = 0;
		// 	Errno e = ProcessFile (basedir+path, OpenFlags.O_RDONLY, delegate (int fd) {
		// 		fixed (byte *pb = buf) {
		// 			return br = (int) Syscall.pread (fd, pb, (ulong) buf.Length, offset);
		// 		}
		// 	});
		// 	bytesRead = br;
		// 	return e;
		// }

		// protected override unsafe Errno OnWriteHandle (string path, OpenedPathInfo info,
		// 		byte[] buf, long offset, out int bytesWritten)
		// {
		// 	int bw = 0;
		// 	Errno e = ProcessFile (basedir+path, OpenFlags.O_WRONLY, delegate (int fd) {
		// 		fixed (byte *pb = buf) {
		// 			return bw = (int) Syscall.pwrite (fd, pb, (ulong) buf.Length, offset);
		// 		}
		// 	});
		// 	bytesWritten = bw;
		// 	return e;
		// }

		// protected override Errno OnGetFileSystemStatus (string path, out Statvfs stbuf)
		// {
		// 	int r = Syscall.statvfs (basedir+path, out stbuf);
		// 	if (r == -1)
		// 		return Stdlib.GetLastError ();
		// 	return 0;
		// }

		protected override Errno OnReleaseHandle (string path, OpenedPathInfo info)
		{
			return 0;
		}

		protected override Errno OnSynchronizeHandle (string path, OpenedPathInfo info, bool onlyUserData)
		{
			return 0;
		}


		// private static void ShowHelp ()
		// {
		// 	Console.Error.WriteLine ("usage: redirectfs [options] mountpoint basedir:");
		// 	FileSystem.ShowFuseHelp ("redirectfs");
		// 	Console.Error.WriteLine ();
		// 	Console.Error.WriteLine ("redirectfs options:");
		// 	Console.Error.WriteLine ("    basedir                Directory to mirror");
		// }

		private static bool Error (string message)
		{
			Console.Error.WriteLine ("redirectfs: error: {0}", message);
			return false;
		}

		private void FillData ()
		{
			lock (data_im_str_lock) {
				if (data_im_str != null)
					return;
				data_im_str = new byte [data_size];
				for (int i = 0; i < data_im_str.Length; ++i) {
					if ((i % 27) == 0)
						data_im_str [i] = (byte) '\n';
					else
						data_im_str [i] = (byte) ((i % 26) + 'a');
				}
			}
		}

		// private bool ParseArguments (string[] args)
		// {
		// 	for (int i = 0; i < args.Length; ++i) {
		// 		switch (args [i]) {
		// 			case "-h":
		// 			case "--help":
		// 				ShowHelp ();
		// 				return false;
		// 			default:
		// 				if (base.MountPoint == null)
		// 					base.MountPoint = args [i];
		// 				else
		// 					basedir = args [i];
		// 				break;
		// 		}
		// 	}
		// 	if (base.MountPoint == null) {
		// 		return Error ("missing mountpoint");
		// 	}
		// 	if (basedir == null) {
		// 		return Error ("missing basedir");
		// 	}
		// 	return true;
		// }



		public static void Main (string[] args)
		{
			using (HelloFS fs = new HelloFS ()) {
				string[] unhandled = fs.ParseFuseArguments (args);
				foreach (string key in fs.FuseOptions.Keys) {
					Console.WriteLine ("Option: {0}={1}", key, fs.FuseOptions [key]);
				}
				if (!fs.ParseArguments (unhandled))
					return;
				// fs.MountAt ("path" /* , args? */);
				fs.Start ();
			}
		}
	}
}

