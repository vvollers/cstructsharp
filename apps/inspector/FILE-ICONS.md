# Schema list icons

[file-type-icons.ts](src/file-type-icons.ts) assigns every catalog extension a dedicated or family
icon. It uses locally bundled VS Code Icons through Iconify. The generic file icon is reserved for
future, unclassified extensions; none of the current 185 catalog entries uses it.

Assignments use the file's purpose, not the container schema name. For example, DOCX uses a document
icon instead of ZIP, HEIC uses an image icon instead of an MP4/video icon, and MPEG video uses the
video icon instead of the audio icon. The filter also searches the assigned family names.

## References and deliberate family substitutions

- [VS Code Icons' extension associations](https://github.com/vscode-icons/vscode-icons/wiki/ListOfFiles)
  supply dedicated matches for fonts, SQLite, Word, Excel, PowerPoint, Photoshop, GIMP, Blender,
  FBX, glTF, EPUB, Java, WebAssembly and OpenPGP, plus general image/audio/video/archive icons.
- [Khronos glTF](https://www.khronos.org/gltf/) identifies GLB/glTF as 3D assets. Other CAD/mesh files
  (DWG, SKP, STL, Draco and 3MF) use the OpenSCAD geometric-solid icon as a **3D family** substitute,
  not an assertion that they are OpenSCAD files.
- [JMP data tables](https://www.jmp.com/en/learning-library/topics/using-jmp/jmp-data-tables) and
  [SPSS SAV](https://www.loc.gov/preservation/digital/formats/fdd/fdd000469.shtml) are statistical
  datasets. They use the general data-storage icon, alongside Arrow, Parquet and Avro.
- [MXF](https://www.loc.gov/preservation/digital/formats/fdd/fdd000013.shtml) is a professional media
  container and uses the video/media family icon. Multipurpose Ogg variants use the media icon,
  while audio-specific Ogg/Opus/Speex variants use audio.
- [ExifTool MIE](https://exiftool.org/TagNames/MIE.html) stores metadata and can encapsulate other
  content; it uses the configuration/metadata icon. ICC profiles and registry/shortcut metadata
  also use this family icon instead of a blank file.
- [Wireshark capture files](https://www.wireshark.org/docs/wsug_html_chunked/_files_and_folders.html)
  contain captured packets. PCAP uses the HTTP/network icon as a networking-family substitute;
  this does not imply the capture contains only HTTP.
- Calendar/contact files share the Outlook personal-information icon with PST mail archives.
  PostScript/EPS share the PDF print-document icon. MOBI and CHM share the EPUB book icon.
  InDesign and Visio share the drawing/page-layout icon. Disk images use the package icon.
  These are family approximations where this icon set has no dedicated format icon.

Icons are navigational hints and do not change schema coverage or parsing behavior.
