import { describe, expect, it } from 'vitest';
import { PosPlace, PosStay } from '../../core/pos';
import { addDays, dayRange, roomsOf, seatRows, stayOn } from './pos-reserve-logic';

const place = (id: string, parentId: string | null, holdsPlaces = false) => ({ id, parentId, holdsPlaces, name: id, code: id }) as PosPlace;
const stay = (roomId: string, fromDate: string, toDate: string) => ({ id: `${roomId}${fromDate}`, roomId, fromDate, toDate }) as PosStay;

describe('reservations', () => {
  it('lays out an auditorium as its rows of seats', () => {
    const places = [place('hall', null, true), place('a', 'hall', true), place('a1', 'a'), place('a2', 'a'), place('b', 'hall', true), place('b1', 'b')];
    expect(seatRows(places, 'hall').map(r => [r.name, r.seats.map(s => s.id)])).toEqual([['a', ['a1', 'a2']], ['b', ['b1']]]);
  });

  it('finds the rooms of a hotel, whatever floors they are on', () => {
    expect(roomsOf([place('f1', null, true), place('101', 'f1'), place('f2', null, true), place('201', 'f2')]).map(r => r.id)).toEqual(['101', '201']);
  });

  it('counts days on the calendar, over a month and a year', () => {
    expect(addDays('2026-10-31', 1)).toBe('2026-11-01');
    expect(dayRange('2026-12-31', 2)).toEqual(['2026-12-31', '2027-01-01']);
  });

  it('a room is taken from its first night up to the day it is left, free that night', () => {
    const stays = [stay('101', '2026-11-01', '2026-11-03')];
    expect(stayOn(stays, '101', '2026-11-01')).toBe(stays[0]);
    expect(stayOn(stays, '101', '2026-11-02')).toBe(stays[0]);
    expect(stayOn(stays, '101', '2026-11-03')).toBeNull();
    expect(stayOn(stays, '201', '2026-11-01')).toBeNull();
  });
});
