/*
 * Where a problem sits in the track, as marks on the waveform's own x-axis.
 *
 * The input is the analysis contract's shape, where a lane names its
 * finding by id rather than living under the finding's code.
 */

// how much of each end a label is taken to cover. the label is ~60px and a
// track several hundred wide, so an eighth is generous -- which is right: the
// cost of being wrong is a collision, the cost of caution is none
export const LABEL_ZONE = 0.12;

/*
 * Consecutive lit buckets collapsed into one mark each: ~1600 buckets is finer
 * than the bar is wide, and an element per bucket would be a thousand nodes
 * redrawn on every resize to paint a handful of blocks.
 */
export const runs = (lane) => {
  const out = [];
  const total = lane.length;
  let index = 0;

  while (index < total) {
    if (!lane[index]) {
      index++;
      continue;
    }

    let end = index;
    let sum = 0;

    while (end < total && lane[end]) {
      sum += lane[end];
      end++;
    }

    out.push({
      from: index / total,
      to: end / total,
      weight: sum / (end - index),
    });
    index = end;
  }

  return out;
};

/*
 * Which end the label goes at: whichever is emptier, ties to the left where the
 * eye starts. Decided once from the data -- a label that moved while being read
 * would be worse than one occasionally over a mark.
 */
export const labelSide = (lane) => {
  if (!lane.length) {
    return 'left';
  }

  const width = Math.max(1, Math.round(lane.length * LABEL_ZONE));
  let head = 0;
  let tail = 0;

  for (let index = 0; index < width; index++) {
    head += lane[index] ?? 0;
  }

  for (let index = lane.length - width; index < lane.length; index++) {
    tail += lane[index] ?? 0;
  }

  return tail < head ? 'right' : 'left';
};

// floored well above nothing: a faint mark is still a fact, and one drawn at 5%
// is a fact nobody can see
export const markOpacity = (weight) =>
  Number((0.4 + 0.6 * (weight / 100)).toFixed(2));

/*
 * The lanes worth drawing, in the order the provider sent them (worst first,
 * so the lane that matters most sits nearest the waveform), each with its
 * finding for the label and the colour. A lane naming no finding has nothing to
 * be labelled by, and an empty one is not a lane.
 */
export const lanesFor = (analysis) => {
  if (!analysis?.lanes?.length) {
    return [];
  }

  const findings = new Map(
    (analysis.findings ?? []).map((finding) => [finding.id, finding]),
  );

  return analysis.lanes
    .filter(
      (lane) =>
        findings.has(lane.finding) &&
        Array.isArray(lane.buckets) &&
        lane.buckets.some(Boolean),
    )
    .map((lane) => ({
      finding: findings.get(lane.finding),
      lane: lane.buckets,
      runs: runs(lane.buckets),
      side: labelSide(lane.buckets),
    }));
};

// the buckets of one finding's lane, or undefined -- what the clip light reads
export const laneOf = (analysis, id) =>
  analysis?.lanes?.find((lane) => lane.finding === id)?.buckets;
