# ADR 0003: Use Windows on-device face detection for portrait preview

## Status

Accepted for Pilot Capture 0.1.

## Context

Pilot Capture's portrait workflow must immediately show an enlarged crop of a detected face while retaining the images for selection. Face review is not used for action workflows. Capture must continue to work offline, and the 0.1 architecture does not require cloud services or a general-purpose AI inference stack.

## Decision

Use the Windows `Windows.Media.FaceAnalysis.FaceDetector` API for local face bounding boxes. Limit detection input to a 1600-pixel maximum dimension, select the largest detected face, and expand the crop to include head and shoulder context. Keep the original JPEG unchanged. If detection is unavailable or finds no face, show the full image and leave review actions available.

Primary and Banner are independent image roles. The first image in a capture set becomes Primary by default; a photographer can change Primary, mark or unmark Banner, and reject an image. Rejected images remain associated with their capture set. Rejecting the current Primary clears its roles and promotes the earliest non-rejected image when one exists.

The Next subject action completes the current capture set and advances to the next membership in the group's displayed roster order. Reaching the end of the group completes the current set and returns control to manual subject selection.

## Consequences

- The desktop project targets Windows 10 SDK APIs and remains intended for Windows 11 pilot use. `EnableWindowsTargeting` keeps cross-compilation available from development systems that do not run Windows.
- Face detection runs on the computer and does not identify a person, compare people, or send image data over the network.
- Detection quality can vary with pose, obstruction, and lighting. The full-image fallback prevents detection failure from blocking capture or review.
- OpenCV/ONNX remains unnecessary for this feature and is not added as a 0.1 dependency.
