# Media Library Optimizer

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
* Volume map `/fastDrive` to your ssd or some other directory separate from your library path.
* Container Variable`NVIDIA_VISIBLE_DEVICES` set to your desired Nvidia GPU (Intel Arc Support Soon.)
* Set the extra parameters to `--runtime=nvidia`

### Requirements
Only supports hardware accelerated Encoding. If you intend to use the Optimizer for encoding, you will need a Nvidia or Intel GPU capable of AV1 Encoding.
Intended to run in a Docker Environment

### Functionality

#### Setup File
When you first run the Optimizer, it will generate a Config.yml file inside of it's config folder that you specified.
The file will look similar to this:
```
encodeHevc: true
encodeAv1: false
remuxDolbyVision: true
libraryPaths:
- {Insert Your Library Path Here}
checkAll: y
startHour: 13
```


The libraryPaths variable is a list, so you can add multiple paths that the Optimizer will process.

#### AV1 Encode:


#### HEVC Encode:


#### Dolby Vision Remux:


