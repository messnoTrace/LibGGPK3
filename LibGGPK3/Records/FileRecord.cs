﻿using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

using SystemExtensions;
using SystemExtensions.Streams;

namespace LibGGPK3.Records {
	/// <summary>
	/// Record contains the data of a file.
	/// </summary>
	public class FileRecord : TreeNode {
		/// <summary>FILE</summary>
		public const int Tag = 0x454C4946;

		/// <summary>
		/// Offset in pack file where the raw data begins
		/// </summary>
		public long DataOffset { get; protected set; }
		/// <summary>
		/// Length of the raw file data
		/// </summary>
		public int DataLength { get; protected internal set; }

		[SkipLocalsInit]
		protected internal unsafe FileRecord(int length, GGPK ggpk) : base(length, ggpk) {
			var s = ggpk.baseStream;
			Offset = s.Position - 8;
			var nameLength = s.Read<int>() - 1;
			s.ReadExactly(_Hash, 0, 32);
			if (Ggpk.Record.GGPKVersion == 4) {
				Span<byte> b = stackalloc byte[nameLength * sizeof(int)];
				s.ReadExactly(b);
				Name = Encoding.UTF32.GetString(b);
				s.Seek(4, SeekOrigin.Current); // Null terminator
			} else {
				Name = s.ReadString(nameLength); // UTF16
				s.Seek(2, SeekOrigin.Current); // Null terminator
			}
			DataOffset = s.Position;
			DataLength = Length - (int)(DataOffset - Offset);
			s.Seek(DataLength, SeekOrigin.Current);
		}

		protected internal FileRecord(string name, GGPK ggpk) : base(default, ggpk) {
			Name = name;
			Length = CaculateRecordLength();
		}

		protected override int CaculateRecordLength() {
			return (Name.Length + 1) * (Ggpk.Record.GGPKVersion == 4 ? 4 : 2) + 44 + DataLength; // (4 + 4 + 4 + Hash.Length + (Name + "\0").Length * 2) + DataLength
		}

		[SkipLocalsInit]
		protected internal override unsafe void WriteRecordData() {
			var s = Ggpk.baseStream;
			Offset = s.Position;
			s.Write(Length);
			s.Write(Tag);
			s.Write(Name.Length + 1);
			s.Write(_Hash, 0, /*_Hash.Length*/32);
			if (Ggpk.Record.GGPKVersion == 4) {
				Span<byte> span = stackalloc byte[Name.Length * sizeof(int)];
				s.Write(span[..Encoding.UTF32.GetBytes(Name, span)]);
				s.Write(0); // Null terminator
			} else {
				s.Write(Name);
				s.Write<short>(0); // Null terminator
			}
			DataOffset = s.Position;
			// Actual file content writing of FileRecord isn't here (see Write())
		}

		/// <summary>
		/// Get the file content of this record
		/// </summary>
		public virtual byte[] Read() {
			lock (Ggpk.baseStream) {
				var s = Ggpk.baseStream;
				s.Flush();
				s.Position = DataOffset;
				var buffer = GC.AllocateUninitializedArray<byte>(DataLength);
				s.ReadExactly(buffer, 0, DataLength);
				return buffer;
			}
		}

		/// <summary>
		/// Get a part of the file content of this record
		/// </summary>
		public virtual byte[] Read(Range range) {
			var (offset, length) = range.GetOffsetAndLength(DataLength);
			lock (Ggpk.baseStream) {
				var s = Ggpk.baseStream;
				s.Flush();
				s.Position = DataOffset + offset;
				var buffer = GC.AllocateUninitializedArray<byte>(length);
				s.ReadExactly(buffer, 0, length);
				return buffer;
			}
		}

		/// <summary>
		/// Replace the file content with <paramref name="newContent"/>,
		/// and move this record to a <see cref="FreeRecord"/> with most suitable size, or end of file if not found.
		/// </summary>
		public virtual void Write(ReadOnlySpan<byte> newContent) {
			if (!Hash256.TryComputeHash(newContent, _Hash, out _))
				ThrowHelper.Throw<UnreachableException>("Unable to compute hash of the content"); // _Hash.Length < 32
			lock (Ggpk.baseStream) {
				var s = Ggpk.baseStream;
				if (newContent.Length != DataLength) { // Replace a FreeRecord
					DataLength = newContent.Length;
					WriteWithNewLength();
					// Offset and DataOffset will be set by WriteRecordData() in above method
				} else {
					s.Position = Offset + sizeof(int) * 3;
					s.Write(_Hash, 0, /*_Hash.Length*/32);
				}
				s.Position = DataOffset;
				s.Write(newContent);
				s.Flush();
			}
		}
	}
}