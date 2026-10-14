"""Fit all three lower base modules to the original, CLOSED protection shell.

Offline only: native CAD units are millimetres, native Y is vertical. A padded
convex envelope makes the 2 mm clearance include the housing and mounting rails,
not just the effective hit rectangle. Production receives ordinary JSON anchors.
"""
import struct
import numpy as np


def lower_armor_fit(vertices, triangles, groups, armor_path):
    import vtk
    from vtk.util import numpy_support as ns
    from scipy.spatial import ConvexHull

    data = armor_path.read_bytes()
    magic, count, size = struct.unpack_from('<8sII', data)
    assert magic == b'TCRMESH2'
    mesh = np.frombuffer(data, '<f4', count * 3, 16).reshape(-1, 3).astype(float) * 1000
    faces = np.frombuffer(data, '<u4', size, 16 + count * 12).reshape(-1, 3).astype(np.int64)
    clearance, height, old_radius = 2., 350., 160.
    # Minkowski sum with a 2 mm cube contains the 2 mm Euclidean offset.
    offsets = np.array([[x, y, z] for x in (-clearance, clearance)
                        for y in (-clearance, clearance) for z in (-clearance, clearance)])
    padded = (mesh[:, None, :] + offsets[None, :, :]).reshape(-1, 3)
    padded_faces = ConvexHull(padded).simplices

    def poly(points, indices):
        p = vtk.vtkPoints(); p.SetData(ns.numpy_to_vtk(np.asarray(points, dtype=float), deep=True))
        cells = vtk.vtkCellArray()
        cells.ImportLegacyFormat(ns.numpy_to_vtkIdTypeArray(
            np.column_stack((np.full(len(indices), 3), indices)).astype(np.int64).ravel(), deep=True))
        result = vtk.vtkPolyData(); result.SetPoints(p); result.SetPolys(cells)
        return result

    closed = poly(vertices, triangles[np.isin(groups, [1, 2, 3, 4, 5, 6])])
    xyz = vertices[triangles]
    cross = np.cross(xyz[:, 1] - xyz[:, 0], xyz[:, 2] - xyz[:, 0])
    area = np.linalg.norm(cross, axis=1)
    units = cross / np.maximum(area[:, None], 1e-9)
    centers = xyz.mean(1)
    details, limits = [], []
    for number in range(3):
        angle = np.deg2rad(number * 120)
        radial = np.array([np.sin(angle), 0., np.cos(angle)])
        normal = radial * np.cos(np.deg2rad(15)) + [0, np.sin(np.deg2rad(15)), 0]
        up = np.array([0, np.cos(np.deg2rad(15)), 0]) - radial * np.sin(np.deg2rad(15))
        basis = np.column_stack((np.cross(up, normal), up, normal))
        aligned = (units @ normal > .99999) & (area > 2000) & (centers[:, 1] > 0) & (centers[:, 1] < 280)
        assert aligned.any(), 'Missing broad plinth outer sloping face'
        distances = centers[aligned] @ normal
        outer = float(distances.max())
        on_outer = aligned & (np.abs(centers @ normal - outer) < .02)
        measured_normal = np.average(units[on_outer], weights=area[on_outer], axis=0)
        measured_normal /= np.linalg.norm(measured_normal)
        parallel_error = float(np.rad2deg(np.arccos(np.clip(normal @ measured_normal, -1, 1))))
        assert parallel_error < .001
        plane_limit = (outer - height * normal[1] - mesh[:, 2].max() - clearance) / (radial @ normal)

        def collision_probe(points, indices):
            probe = vtk.vtkCollisionDetectionFilter()
            probe.SetInputData(0, closed); probe.SetInputData(1, poly(points @ basis.T, indices))
            fixed, moving = vtk.vtkTransform(), vtk.vtkTransform()
            probe.SetTransform(0, fixed); probe.SetTransform(1, moving)
            probe.SetCollisionModeToFirstContact(); probe.SetBoxTolerance(0); probe.SetCellTolerance(0)
            def hits(radius, direction=radial.copy()):
                moving.Identity(); moving.Translate(direction * radius + [0, height, 0]); probe.Update()
                return probe.GetNumberOfContacts() > 0
            return hits

        def first_contact(hits):
            # Find FIRST intersection: the shell can be crossed entirely, so a
            # binary search across the whole range is not monotonic/safe.
            assert not hits(old_radius), 'Initial armor intersects the closed shell'
            low = old_radius
            for sample in np.arange(old_radius + 1, plane_limit + 1, 1.):
                high = min(float(sample), plane_limit)
                if hits(high):
                    for _ in range(18):
                        middle = (low + high) / 2
                        if hits(middle): high = middle
                        else: low = middle
                    return low
                low = high
            return plane_limit

        actual_hits = collision_probe(mesh, faces)
        padded_hits = collision_probe(padded, padded_faces)
        safe_limit, touch_limit = first_contact(padded_hits), first_contact(actual_hits)
        limits.append(min(plane_limit, safe_limit))
        details.append(dict(face=number, normal=normal.tolist(), measuredNormal=measured_normal.tolist(),
                            parallelErrorDegrees=parallel_error, outerPlaneOffsetMm=outer,
                            planeRadiusLimitMm=float(plane_limit), closedShellFirstTouchRadiusMm=touch_limit,
                            paddedShellRadiusLimitMm=safe_limit, _actual=actual_hits, _padded=padded_hits))
    # All three modules stay symmetric. Round down, never through the bound.
    radius = float(np.floor(min(limits) * 10) / 10)
    for item in details:
        assert not item.pop('_actual')(radius)
        assert not item.pop('_padded')(radius)
        item['outerPlaneClearanceMm'] = item['outerPlaneOffsetMm'] - (
            height * np.sin(np.deg2rad(15)) + radius * np.cos(np.deg2rad(15)) + mesh[:, 2].max())
        item['closedShellIntersection'] = False
        item['paddedShellIntersection'] = False
    return dict(heightMm=height, previousRadiusMm=old_radius, radiusMm=radius,
                horizontalOutwardMm=radius-old_radius, minimumClosedShellClearanceMm=clearance,
                method='Original closed-cover triangle collision against complete module and 2mm padded convex envelope; broad plinth outer plane limit; first contact along outward sweep',
                faces=details)
