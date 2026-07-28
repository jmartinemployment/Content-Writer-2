import Link from "next/link";

export default function HomePage() {
  return (
    <section className="hero">
      <h1>Write once. Shape the voice. Ship the piece.</h1>
      <p className="lede">
        Pick a template, fill a short form, generate real copy, then edit and save.
        Brand voices steer tone; usage keeps cost visible.
      </p>
      <div className="actions" style={{ marginTop: "1.5rem" }}>
        <Link className="button" href="/templates">
          Browse templates
        </Link>
        <Link className="button secondary" href="/documents">
          Open documents
        </Link>
      </div>
    </section>
  );
}
