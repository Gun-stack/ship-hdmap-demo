package com.shiphdmap.api.model;

import java.util.List;

/** Body of POST /api/datasets/{id}/seed. The Unity generator sends the first five lists; a full fixture may also carry slots and landmarks. */
public record SeedData(List<VehicleMap.Deck> decks, List<VehicleMap.Facility> facilities, List<VehicleMap.LashingPoint> lashingPoints,
		List<VehicleMap.Ramp> ramps, List<VehicleMap.Lane> lanes, List<VehicleMap.ParkingSlot> parkingSlots, List<VehicleMap.Landmark> landmarks) {}
