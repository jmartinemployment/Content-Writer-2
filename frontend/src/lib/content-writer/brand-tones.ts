/** Mirrors backend BrandTones preset ids/labels. */
export const BRAND_TONES = [
  { id: "webpages", label: "Webpages — Clear and Reassuring" },
  { id: "email", label: "Email — Personal and Direct" },
  { id: "linkedin", label: "LinkedIn — Expert and Collaborative" },
  { id: "facebook", label: "Facebook — Local and Approachable" },
] as const;

export type BrandToneId = (typeof BRAND_TONES)[number]["id"];
