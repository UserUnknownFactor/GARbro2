NVorbis    
-------

NVorbis is a .Net library for decoding Xiph.org Vorbis files. It's designed to run in partial trust environments and does not require P/Invoke or unsafe code.  
It's built for .Net Standard 2.1 and .Net Framework 4.8.

This implementation is based on the Vorbis specification found on xiph.org. The MDCT and Huffman codeword generator were borrowed from public domain implementations in [stb_vorbis.c](https://github.com/nothings/stb/blob/master/stb_vorbis.c).

If you're using [NAudio](https://github.com/naudio/NAudio), support is available via [NAudio.Vorbis](https://github.com/NAudio/Vorbis).