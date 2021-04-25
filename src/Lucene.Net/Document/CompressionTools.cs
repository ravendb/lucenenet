/* 
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 * 
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */


// To enable compression support in Lucene.Net ,
// you will need to define 'SHARP_ZIP_LIB' and reference the SharpLibZip 
// library.  The SharpLibZip library can be downloaded from: 
// http://www.icsharpcode.net/OpenSource/SharpZipLib/

using System;
using System.Buffers;
using Lucene.Net.Support;
using UnicodeUtil = Lucene.Net.Util.UnicodeUtil;

namespace Lucene.Net.Documents
{
	
	/// <summary>Simple utility class providing static methods to
	/// compress and decompress binary data for stored fields.
	/// This class uses java.util.zip.Deflater and Inflater
	/// classes to compress and decompress.
	/// </summary>
	
	public class CompressionTools
	{
		
		// Export only static methods
		private CompressionTools()
		{
		}
		
		/// <summary>Compresses the specified byte range using the
		/// specified compressionLevel (constants are defined in
		/// java.util.zip.Deflater). 
		/// </summary>
		public static byte[] Compress(Span<byte> value_Renamed, int compressionLevel)
		{
			/* Create an expandable byte array to hold the compressed data.
			* You cannot use an array that's the same size as the orginal because
			* there is no guarantee that the compressed data will be smaller than
			* the uncompressed data. */
			System.IO.MemoryStream bos = new System.IO.MemoryStream(value_Renamed.Length);

            Deflater compressor = SharpZipLib.CreateDeflater();

			compressor.SetLevel(compressionLevel);
			compressor.SetInput(value_Renamed);
			compressor.Finish();
				
			// Compress the data
			byte[] buf = ArrayPool<byte>.Shared.Rent(1024);
            try
            {
				while (!compressor.IsFinished)
				{
					int count = compressor.Deflate(buf);
					bos.Write(buf, 0, count);
				}
			}
            finally
            {
				ArrayPool<byte>.Shared.Return(buf);
            }
			
			return bos.ToArray();
		}
		
		/// <summary>Compresses all bytes in the array, with default BEST_COMPRESSION level </summary>
		public static byte[] Compress(Span<byte> value_Renamed)
		{
            return Compress(value_Renamed, Deflater.BEST_COMPRESSION);
		}
		
		/// <summary>Compresses the String value, with default BEST_COMPRESSION level </summary>
		public static byte[] CompressString(System.String value_Renamed)
		{
            return CompressString(value_Renamed, Deflater.BEST_COMPRESSION);
		}
		
		/// <summary>Compresses the String value using the specified
		/// compressionLevel (constants are defined in
		/// java.util.zip.Deflater). 
		/// </summary>
		public static byte[] CompressString(System.String value_Renamed, int compressionLevel)
		{
            using (UnicodeUtil.UTF8Result result = new UnicodeUtil.UTF8Result())
            {
                UnicodeUtil.UTF16toUTF8(value_Renamed, 0, value_Renamed.Length, result);
                return Compress(result.result.Memory.Span.Slice(0, result.length), compressionLevel);
            }
        }
		
		/// <summary>Decompress the byte array previously returned by
		/// compress 
		/// </summary>
		public static Memory<byte> Decompress(Span<byte> value_Renamed)
		{
			// Create an expandable byte array to hold the decompressed data
			System.IO.MemoryStream bos = new System.IO.MemoryStream(value_Renamed.Length);
			
			Inflater decompressor = SharpZipLib.CreateInflater();
			
			decompressor.SetInput(value_Renamed);
				
			// Decompress the data
			byte[] buf = ArrayPool<byte>.Shared.Rent(1024);
			try
            {
				while (!decompressor.IsFinished)
				{
					int count = decompressor.Inflate(buf);
					bos.Write(buf, 0, count);
				}
			}
            finally
            {
				ArrayPool<byte>.Shared.Return(buf);
            }
			
			return bos.ToArray();
		}
		
		/// <summary>Decompress the byte array previously returned by
		/// compressString back into a String 
		/// </summary>
		public static System.String DecompressString(Span<byte> value_Renamed)
		{
            using (UnicodeUtil.UTF16Result result = new UnicodeUtil.UTF16Result())
            {
                Memory<byte> bytes = Decompress(value_Renamed);
                UnicodeUtil.UTF8toUTF16(bytes.Span, 0, bytes.Length, result);
                return new System.String(result.result.Memory.Span.Slice(0, result.length));
            }
        }
	}
}

