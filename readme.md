Created out of my own self-interest but also as a lighter, faster, and more feature-restricted alternative to DotNetZip.
Made with Apple's IPA archives in mind.

## Features:
- Read existing zip archives
- Create new zip archives
- Update new or existing entries within a zip archive
- Per-entry compression
- Parallel compression
- Support for Windows and Unix platforms, including their respective file attributes and permissions

## Does not support:
- Encryption
- LocalFileHeader / CentralDirectory extra fields, which usually houses high-precision timestamp data.
- Zip64

## Credits
- PKWARE's [ZIP specification](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT)
