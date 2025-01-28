# Media Library Optimizer

<img src="Original Logo.png" alt="Media Library Optimizer Logo">

## An application to shrink the size of your media servers storage space without perceivable quality loss or artefacting!

The Optimizer will shrink your servers storage while retaining very high quality.
* *Tested on 65 inch Hisense U8N, no noticable compression artefacts.*
* *Tested with large HEVC files 50+GB often compressing down to half size with a good amount compressing even more!*

### Main Functions
1. Convert a non AV1 and non Dolby Vision video file to AV1.
2. Remux a Dolby Vision profile 7 video to Dolby Vision profile 8. No encoding, will retain all quality.
3. Encode a Dolby Vision video to HEVC.
<br><br>*Note, best storage savings with large uncompressed files.*

## Quick Setup With Docker
<a>https://hub.docker.com/r/slummybell/media-library-optimizer</a>
<br>
`docker pull slummybell/media-library-optimizer`
<br>
* Volume map `/config` to your desired configuration path on your machine.
* Volume map `/incomplete` to your ssd or some other directory separate from your library path. (ssd prefered due to drive speed)
* Container Variable`NVIDIA_VISIBLE_DEVICES` set to your desired Nvidia GPU (Intel Arc Support Soon.)
* Set the extra parameters to `--runtime=nvidia`
* Run container and modify the generated setup file from below.

### Setup File
When you first run the Optimizer, it will generate a Config.yml file inside of it's config folder that you specified.
The file will look similar to this:
```Config.yml
encodeHevc: true
encodeAv1: false
remuxDolbyVision: true
libraryPaths:
- {Insert Your Library Path Here}
checkAll: y
startHour: 13
retryFailed: false
```

`libraryPaths` variable is a list, so you can add multiple paths that the Optimizer will process.
```libraryPathsExample
libraryPaths:
- /movies
- /tvShows
```
`retryFailed` if set to true will allow the program to process files previously marked as failed.

### Requirements
Only supports hardware accelerated Encoding. If you intend to use the Optimizer for encoding, you will need a Nvidia or Intel GPU capable of AV1 Encoding.
<br>
Intended to run in a Docker Environment.

## Functionality
Before any of these proccesses are ran, the file will get moved to the `/incomplete` location.
<br>
For all encodes, the program will check if the output generated is larger than the input and discard the larger output and mark the encode as failed.
### AV1 Encode:
*Note: AV1_NVENC*
<br>
If the file is Non Dolby Vision and is not in AV1:
<br>
The program will build out the ffmpeg command:
`ffmpeg -i '{filePath}' -map 0:v:0 -map 0:a? -map 0:s? -c:v av1_nvenc -cq 25 -preset p7 -c:a copy -c:s copy -map_metadata 0 -map_chapters 0 '{_commandOutputFile}'`
<br>
The `cq` value of 25 is great for most content. However, files with low bitrates (less than 12mbps for example) on a `cq` of 25 are often encoded to larger file sizes than the input. So, for files with bitrates:
* Greater than 11mbps: `cq 25`
* Between 11mbps and 7mbps: `cq 29`
* Less than 7mbps: `cq 32`
### HEVC Encode:
*Note: HEVC_NVENC*
<br>
If the file is Dolby Vision 8 or 5:
<br>
The program will build out the ffmpeg command for the encode:
`ffmpeg -i '{_hevcFile}' -c:v hevc_nvenc -preset p7 -cq 3 -c:a copy '{_encodedHevc}'`
<br>
However, running that command alone will strip the Dolby Vision metadata.
1. Extracts HEVC Stream to a separate `.hevc` file.
2. Extracts RPU metadata to a separate `.rpu` file from the `.hevc` file.
3. Encodes the `.hevc` file to another `encoded.hevc` file.
4. Injects the RPU metadata into `encoded.hevc`.
5. Remuxes `encoded.hevc` into the input `.mkv` retaining all metadata and other streams and generating `output.mkv`

### Dolby Vision Remux:
If the file is Dolby Vision 7:

1. Extracts HEVC Stream to a separate `.hevc` file.
2. Extracts RPU metadata to a separate `.rpu` file from the `.hevc` file.
3. Converts the Dolby Vision Profile 7 `.rpu` to a `profile8.rpu` file.
4. Injects the `profile8.rpu` into `.hevc`.
5. Remuxes `.hevc` into the input `.mkv` retaining all metadata and other streams and generating `output.mkv`
### Miscelanious Functionality:

#### Metadata Generation
After a file is proccessed, weather failed or successufully, the program will add a small bit of Metadata to the container:
<br>
`LIBRARY_OPTIMIZER_APP=Converted={conversion status true or false}. Reason={Reason for conversion status}`
<br>
This is added to prevent the program from reproccessing files that have already been proccessed or files that have previously failed (you can tell it to ignore the failed check using the `retryFailed` in the `Config.yml`)
