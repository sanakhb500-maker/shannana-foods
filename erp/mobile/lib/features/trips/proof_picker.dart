import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:signature/signature.dart';

import '../../core/strings.dart';

class ProofData {
  ProofData(this.kind, this.bytes, this.filename);

  final String kind; // Signature | Photo, as the API expects
  final Uint8List bytes;
  final String filename;
}

/// Holds what the driver captured (a drawn signature or a photo). `resolve()` turns it into bytes to upload, or null when empty.
class ProofController extends ChangeNotifier {
  final SignatureController signature = SignatureController(penStrokeWidth: 3, penColor: Colors.black, exportBackgroundColor: Colors.white);
  Uint8List? photo;
  String photoName = 'photo.jpg';
  bool usePhoto = false;

  void setMode(bool photoMode) {
    usePhoto = photoMode;
    notifyListeners();
  }

  void setPhoto(Uint8List bytes, String name) {
    photo = bytes;
    photoName = name;
    notifyListeners();
  }

  void clear() {
    if (usePhoto) {
      photo = null;
    } else {
      signature.clear();
    }
    notifyListeners();
  }

  Future<ProofData?> resolve() async {
    if (usePhoto) return photo == null ? null : ProofData('Photo', photo!, photoName);
    if (signature.isEmpty) return null;
    final png = await signature.toPngBytes();
    return png == null ? null : ProofData('Signature', png, 'signature.png');
  }

  @override
  void dispose() {
    signature.dispose();
    super.dispose();
  }
}

/// Signature pad or camera, switchable. Both are optional proofs of delivery.
class ProofPicker extends StatelessWidget {
  const ProofPicker({super.key, required this.controller});

  final ProofController controller;

  Future<void> _take(BuildContext context) async {
    final file = await ImagePicker().pickImage(source: ImageSource.camera, maxWidth: 1280, imageQuality: 70);
    if (file == null) return;
    controller.setPhoto(await file.readAsBytes(), file.name.isEmpty ? 'photo.jpg' : file.name);
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return ListenableBuilder(
      listenable: controller,
      builder: (context, _) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        SegmentedButton<bool>(
          segments: [
            ButtonSegment(value: false, label: Text(context.t('deliver.signature')), icon: const Icon(Icons.draw_outlined)),
            ButtonSegment(value: true, label: Text(context.t('deliver.photo')), icon: const Icon(Icons.photo_camera_outlined)),
          ],
          selected: {controller.usePhoto},
          onSelectionChanged: (s) => controller.setMode(s.first),
        ),
        const SizedBox(height: 12),
        if (!controller.usePhoto)
          Container(
            height: 190,
            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18), border: Border.all(color: scheme.outlineVariant, width: 1.5)),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(17),
              child: Stack(children: [
                Signature(controller: controller.signature, backgroundColor: Colors.white),
                IgnorePointer(
                  child: Center(child: Text(context.t('deliver.signHere'), style: TextStyle(color: Colors.black.withValues(alpha: 0.18), fontSize: 22, fontWeight: FontWeight.w700))),
                ),
              ]),
            ),
          )
        else if (controller.photo != null)
          ClipRRect(borderRadius: BorderRadius.circular(18), child: Image.memory(controller.photo!, height: 220, fit: BoxFit.cover))
        else
          OutlinedButton.icon(
            onPressed: () => _take(context),
            style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(120)),
            icon: const Icon(Icons.photo_camera_outlined, size: 30),
            label: Text(context.t('deliver.takePhoto')),
          ),
        const SizedBox(height: 8),
        Row(children: [
          TextButton.icon(onPressed: controller.clear, icon: const Icon(Icons.delete_outline), label: Text(context.t('deliver.clear'))),
          if (controller.usePhoto && controller.photo != null)
            TextButton.icon(onPressed: () => _take(context), icon: const Icon(Icons.photo_camera_outlined), label: Text(context.t('deliver.takePhoto'))),
        ]),
      ]),
    );
  }
}
