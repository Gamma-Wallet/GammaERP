import { PosPlace, PosStay } from '../../core/pos';

/** A row of seats of an auditorium. */
export interface SeatRow {
  id: string;
  name: string;
  seats: PosPlace[];
}

/** The rows of seats under an auditorium: its rows, each with the seats in it. */
export function seatRows(places: readonly PosPlace[], auditoriumId: string): SeatRow[] {
  return places.filter(p => p.parentId === auditoriumId)
    .map(row => ({ id: row.id, name: row.name ?? row.code ?? '', seats: places.filter(p => p.parentId === row.id) }));
}

/** The rooms under a hotel: its places with nothing under them. */
export function roomsOf(places: readonly PosPlace[]): PosPlace[] {
  return places.filter(p => !p.holdsPlaces && !places.some(q => q.parentId === p.id));
}

/** A day as yyyy-MM-dd, moved by some days; on the calendar, so a change of the clocks moves nothing. */
export function addDays(day: string, days: number): string {
  const [y, m, d] = day.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/** The days from one, in order. */
export function dayRange(from: string, count: number): string[] {
  return Array.from({ length: count }, (_, i) => addDays(from, i));
}

/** The stay a room has on a night: from its first night up to, not including, the day it is left. */
export function stayOn(stays: readonly PosStay[], roomId: string, day: string): PosStay | null {
  return stays.find(s => s.roomId === roomId && s.fromDate <= day && day < s.toDate) ?? null;
}
