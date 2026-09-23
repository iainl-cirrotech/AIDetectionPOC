# Routed Image Detection — Draft Specification

**Status:** Starting point for discussion
**Last updated:** 23 September 2026

## 1. Background

The current proof of capability applies one general AI-image classifier to each submitted image. This works well for some fully generated product and damaged-order images, but it can miss localized changes to an otherwise realistic photograph.

One reported skin-reaction example scored 6.26% using the current centre-crop inference. Correcting its orientation or analysing a crop containing more of the affected skin raised the score to approximately 49–50%. This indicates two potential improvements:

1. analyse multiple orientations and regions rather than one centre crop; and
2. route different complaint-image categories to appropriately validated specialist detectors.

The system remains a screening aid for customer-service review. It must not determine that a complaint is fraudulent, that an image is genuine, or that a person has a medical condition.

## 2. Objectives

- Identify whether an image contains human skin/a person, a product/parcel, both, or neither.
- Run a general forensic detector for every supported image.
- Run additional specialist detectors when the content router has sufficient confidence.
- Improve sensitivity to localized AI modifications, particularly simulated redness, rashes, damage, leakage, and defects.
- Preserve an auditable explanation of routing, detector scores, provenance, and the final screening result.
- Measure improvement against a representative labelled evaluation set before operational use.

## 3. Non-goals

- Medical diagnosis or assessment of the severity or authenticity of a skin reaction.
- Automatic rejection, approval, or fraud determination for a customer complaint.
- Identifying the person shown in an image.
- Treating absent metadata or Content Credentials as evidence of manipulation.
- Guaranteeing detection of all generated or edited images.

## 4. Proposed processing flow

```text
Submitted image
  -> validation and metadata/provenance checks
  -> orientation normalization
  -> semantic content router
       -> skin/person specialist when applicable
       -> product/packaging specialist when applicable
       -> both specialists for mixed or overlapping content
  -> general AI-image detector (always)
  -> multi-region forensic analysis
  -> calibrated score combination
  -> Low / Medium / High / Inconclusive screening result
  -> evidence and audit record
```

Routing should select additional analysis, not replace the general detector.

## 5. Semantic content router

### 5.1 Initial categories

| Category | Description | Example destination |
|---|---|---|
| `skin_or_person` | A person, face, body part, or close-up skin image | General detector plus skin specialist |
| `product_or_packaging` | A product, order, parcel, container, or packaging | General detector plus product specialist |
| `mixed` | Both relevant people/skin and products are visible | General detector plus both specialists |
| `other` | Supported image outside the principal complaint categories | General detector only |
| `uncertain` | Router confidence is too low to select a category safely | General detector; record uncertainty |

These labels describe visible content only. `skin_or_person` must not be presented as a diagnosis or claim that a reaction exists.

### 5.2 Router behaviour

- The first POC may use a zero-shot vision model with fixed, version-controlled prompts.
- The router should return a score for every category rather than only one label.
- More than one specialist may be selected when scores overlap.
- Low-confidence routing must fall back to the general detector.
- Router model, version, prompts, category scores, and selected routes must be recorded.
- Thresholds must be configurable and established from evaluation data.

Example output:

```json
{
  "model": "router-model-and-version",
  "scores": {
    "skin_or_person": 0.92,
    "product_or_packaging": 0.04,
    "other": 0.04
  },
  "selectedRoutes": ["skin_or_person"],
  "status": "routed"
}
```

## 6. Orientation and multi-region analysis

The current detector evaluates a single centre crop. The revised design should evaluate content that may be localized or stored in an unexpected orientation.

Candidate preprocessing:

- Apply trustworthy EXIF orientation when present.
- Consider 0°, 90°, 180°, and 270° views when orientation is missing or uncertain.
- Evaluate a centre crop and spatial crops covering the image edges/corners.
- Avoid duplicate crops for dimensions where two crop locations are identical.
- Cap image count and inference time to prevent unbounded processing cost.
- Record the view or region responsible for the strongest signal without retaining unnecessary original image data.

Multi-view aggregation changes the statistical behaviour of the classifier. Existing calibration and Low/Medium/High thresholds must therefore be re-evaluated rather than reused without testing.

## 7. Detector responsibilities

### 7.1 General detector

- Runs for every image.
- Detects broad signs of full-image AI generation or manipulation.
- Continues to provide a useful fallback when routing is uncertain or a specialist is unavailable.

### 7.2 Skin/person specialist

- Focuses on localized manipulation in photorealistic images of people or skin.
- Should use patch-level or localization-aware analysis where practical.
- Detects forensic manipulation signals; it must not classify diseases or diagnose reactions.
- Must be evaluated against genuine skin complaints as well as AI-generated and AI-modified examples across varied skin tones, lighting, cameras, compression levels, and body areas.

### 7.3 Product/packaging specialist

- Focuses on generated or modified damage, leakage, contamination, missing contents, and packaging defects.
- Must be evaluated across the product and packaging types encountered by customer service.

### 7.4 Specialist interface

Each detector should expose a consistent result shape:

```json
{
  "detector": "detector-name",
  "version": "version-or-revision",
  "route": "skin_or_person",
  "score": 0.73,
  "status": "completed",
  "viewsAnalysed": 5,
  "strongestView": "rotate-90/bottom",
  "error": null
}
```

## 8. Combining results

The combined score must not initially be a simple unvalidated maximum. Testing multiple models, crops, and orientations increases the chance of a high score on genuine images.

The combination policy should be developed using labelled validation data. Candidate approaches include:

- thresholds calibrated independently for each route and detector;
- a small calibrated meta-classifier using detector and router scores; or
- a conservative policy that raises a manual-review indicator when multiple independent signals agree.

C2PA provenance should continue to take precedence when a valid declaration identifies trained algorithmic media. Classifier output should remain probabilistic and clearly separated from cryptographic provenance.

If a selected specialist fails, the result should remain available from the general detector and record the specialist failure. It must not silently treat the missing specialist score as negative evidence.

## 9. Proposed result and audit additions

Add the following information to the analysis result:

- normalized-orientation action and source;
- router model and version;
- per-category router scores;
- selected routes and routing threshold;
- each detector's model, version, score, status, and views analysed;
- score-combination policy and version;
- final calibrated screening band;
- processing duration per stage;
- explicit reasons for skipped, unavailable, or failed analysis.

The customer-service view should summarise the result in plain language while allowing technical evidence to be inspected separately.

## 10. Evaluation plan

### 10.1 Dataset

Build a permissioned, labelled evaluation set containing:

- genuine and AI-generated product/packaging complaint images;
- paired original and AI-modified versions where available;
- genuine and AI-modified skin/person complaint images;
- fully generated skin/person images;
- mixed, unrelated, difficult, compressed, cropped, and rotated images;
- examples from the tools and workflows customers are likely to use.

The initial target should be at least 50–100 genuine and 50–100 AI-generated or modified examples for each principal route. This is sufficient for an early POC comparison but not for asserting production-grade performance.

Images of people and possible health conditions require an agreed lawful basis, access controls, retention period, and consent or other appropriate governance before use in evaluation or training.

### 10.2 Measurements

Report measurements separately for each content category and manipulation type:

- router precision, recall, and confusion matrix;
- detector recall for generated and locally modified images;
- false-positive rate on genuine complaints;
- precision and recall at proposed review thresholds;
- calibration error;
- failure rate and latency;
- results by relevant image conditions, without using the system to infer sensitive attributes.

The main acceptance criterion should balance improved detection of manipulated complaints with a tolerable manual-review workload. Overall accuracy alone is not sufficient for an imbalanced dataset.

### 10.3 Baselines and experiment groups

Compare:

1. the current centre-crop general detector;
2. the general detector with orientation and multi-region analysis;
3. semantic routing plus specialists;
4. the complete routed and calibrated pipeline.

This separates the benefit of better preprocessing from the benefit of specialist models.

## 11. Rollout approach

1. **Offline evaluation:** Run all variants against the labelled dataset.
2. **Shadow mode:** Record router and specialist results without changing the visible POC result.
3. **Assisted review:** Show the new result and evidence to selected reviewers without automating complaint outcomes.
4. **Threshold review:** Adjust thresholds from observed false positives, misses, and review volume.
5. **Controlled release:** Adopt the new combination policy with versioned configuration and rollback capability.

## 12. Security, privacy, and operational constraints

- Continue to discard original images after transient processing unless a separately approved evaluation workflow requires retention.
- Retain only the minimum thumbnail and evidence necessary for review and audit.
- Do not send images to an external inference service without approval of its data handling, region, retention, and training-use terms.
- Treat filenames, sender details, faces, and possible health information as sensitive data.
- Set strict file-size, pixel-count, inference-count, and processing-time limits.
- Pin model artifacts, prompts, dependencies, and combination-policy versions.
- Monitor route distribution and score drift after release.

## 13. Initial implementation increments

### Increment A — Evaluation harness

- Add a labelled manifest format and offline batch evaluation command.
- Establish current detector metrics by category.
- Add regression cases for the reported product and skin examples where permission allows.

### Increment B — Multi-view general detector

- Normalize known orientation.
- Add bounded orientation and spatial views.
- Return per-view scores for evaluation.
- Recalibrate aggregation and thresholds using the labelled dataset.

### Increment C — Semantic router

- Add the five initial categories.
- Log scores and proposed routes in shadow mode.
- Establish routing thresholds from the dataset.

### Increment D — Specialist detectors

- Integrate specialists behind the common detector interface.
- Measure incremental benefit over multi-view general detection.
- Add versioned, calibrated result combination.

### Increment E — User experience and operations

- Present routes, evidence, uncertainty, and partial failures clearly.
- Add latency, error, route-distribution, and drift monitoring.
- Document rollback and model-update procedures.

## 14. Open decisions

- Which router model offers acceptable accuracy, latency, licensing, and deployment characteristics?
- Should router inference run locally in the existing container or through an approved hosted service?
- Which specialist models have evidence of performance on localized, photorealistic edits?
- What inference-time budget is acceptable per complaint image?
- How should overlapping router scores select one or multiple specialists?
- What false-positive rate and manual-review volume can customer service support?
- What data may be retained for evaluation, for how long, and under which governance process?
- Should region-level heatmaps be shown to reviewers, given that localization output may itself be uncertain?
- Which result-combination method performs best on representative data?

## 15. Definition of done for the next POC

- Router and detector versions are pinned and recorded.
- The router supports skin/person, product/packaging, mixed, other, and uncertain outcomes.
- The general detector runs regardless of route.
- Multi-view and specialist failures degrade safely and are visible in evidence.
- Offline results demonstrate improvement over the current baseline by category.
- Proposed thresholds include measured false-positive rates and review-volume estimates.
- No automated fraud or medical decision is made.
- Privacy, retention, and operational documentation is updated before use with real complaint data.
