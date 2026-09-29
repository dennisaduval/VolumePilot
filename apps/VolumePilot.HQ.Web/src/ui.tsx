import type { ReactNode } from "react";

export function dateLabel(value: string | null): string {
  if (!value) return "Date not set";
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

export function Banner({
  children,
  tone = "info",
}: {
  children: ReactNode;
  tone?: "info" | "error" | "success";
}) {
  return (
    <div
      className={`banner banner-${tone}`}
      role={tone === "error" ? "alert" : "status"}
    >
      {children}
    </div>
  );
}

export function Empty({
  title,
  children,
}: {
  title: string;
  children: ReactNode;
}) {
  return (
    <div className="empty">
      <strong>{title}</strong>
      <p>{children}</p>
    </div>
  );
}

export function PageTitle({
  kicker,
  title,
  description,
  action,
}: {
  kicker: string;
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div className="page-heading">
      <div>
        <p className="eyebrow">{kicker}</p>
        <h1>{title}</h1>
        <p className="page-subtitle">{description}</p>
      </div>
      {action}
    </div>
  );
}
