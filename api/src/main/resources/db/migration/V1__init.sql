-- Spec §5.4. Ship Frame only (SRID 0, metres); WGS84 is derived at export time.
CREATE EXTENSION IF NOT EXISTS postgis;

CREATE TABLE dataset (
  id          text PRIMARY KEY,
  name        text NOT NULL,
  ship_name   text NOT NULL DEFAULT '',
  version     integer NOT NULL DEFAULT 1,
  ap_lat      double precision NOT NULL DEFAULT 0,
  ap_lon      double precision NOT NULL DEFAULT 0,
  heading_deg double precision NOT NULL DEFAULT 0,
  lpp_m       double precision NOT NULL DEFAULT 120,
  created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE deck (
  dataset_id text NOT NULL REFERENCES dataset ON DELETE CASCADE,
  id         text NOT NULL,
  name       text NOT NULL,
  z_surface  double precision NOT NULL,
  z_clear    double precision NOT NULL,
  movable    boolean NOT NULL DEFAULT false,
  z_current  double precision,
  outline    geometry(PolygonZ, 0) NOT NULL,
  PRIMARY KEY (dataset_id, id)
);

CREATE TABLE feature (
  dataset_id text NOT NULL REFERENCES dataset ON DELETE CASCADE,
  id         text NOT NULL,
  deck_id    text,
  layer      text NOT NULL CHECK (layer IN ('A1', 'A2', 'B2', 'C', 'LM', 'LP', 'MEP')),
  kind       text NOT NULL,
  geom       geometry(GeometryZ, 0) NOT NULL,
  props      jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (dataset_id, id),
  FOREIGN KEY (dataset_id, deck_id) REFERENCES deck (dataset_id, id) ON DELETE SET NULL (deck_id)
);
CREATE INDEX feature_dataset_layer ON feature (dataset_id, layer);
CREATE INDEX feature_geom_gist ON feature USING GIST (geom);

CREATE TABLE parking_slot (
  dataset_id         text NOT NULL,
  feature_id         text NOT NULL,
  target_x           double precision NOT NULL,
  target_y           double precision NOT NULL,
  target_heading_deg double precision NOT NULL,
  tol_lat_m          double precision NOT NULL DEFAULT 0.15,
  tol_lon_m          double precision NOT NULL DEFAULT 0.30,
  tol_heading_deg    double precision NOT NULL DEFAULT 2,
  vehicle_class      text NOT NULL DEFAULT 'passenger',
  sequence_no        integer NOT NULL,
  status             text NOT NULL DEFAULT 'empty' CHECK (status IN ('empty', 'filled', 'needs_adjust')),
  access_lane_id     text,
  lashing_ids        text[] NOT NULL DEFAULT '{}',
  PRIMARY KEY (dataset_id, feature_id),
  FOREIGN KEY (dataset_id, feature_id) REFERENCES feature (dataset_id, id) ON DELETE CASCADE,
  FOREIGN KEY (dataset_id, access_lane_id) REFERENCES feature (dataset_id, id) ON DELETE SET NULL (access_lane_id)
);
