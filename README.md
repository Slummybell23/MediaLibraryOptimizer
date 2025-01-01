# Media Library Optimizer

## An application to largly shrink the size of your media servers storage space without perceivable quality loss or artefacting!

The Optimizer will shrink your servers storage while retaining very high quality.
* Tested on Hisense U8N, no noticable compression artefacts.
* Tested with large HEVC files 50+GB often compressing down to half size with a good amount compressing even more!

* 1: Convert a non AV1 and non Dolby Vision video file to AV1.
* 2: Remux a Dolby Vision profile 7 video to Dolby Vision profile 8. No encoding, will retain all quality.
* 3: Encode a Dolby Vision video to HEVC.
Any of these options can be enabled or disabled as neccessary.

If you suffer from a storage server full of video files and you don't want to delete them, you're in the right place.
** Note, best storage savings with large uncompressed files.

### Requirements:
Only supports hardware accelerated Encoding. If you intend to use the Optimizer for encoding, you will need a Nvidia or Intel GPU capable of AV1 Encoding.
Intended to run in a Docker Environment

### Functionality:

#### Setup File:
When you first run the Optimizer, it will generate a Config.yml file inside of it's config folder that you specified

#### AV1 Encode:


#### HEVC Encode:


#### Dolby Vision Remux:

