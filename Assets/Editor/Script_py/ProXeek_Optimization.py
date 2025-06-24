#!/usr/bin/env python3
"""
ProXeek Global Optimization Module

This module implements the global optimization stage for haptic proxy assignment,
implementing the Multi-Objective Loss Function from the backend specification.
"""

import os
import sys
import json
import numpy as np
import itertools
from typing import List, Dict, Any, Tuple, Optional
from dataclasses import dataclass
import time

@dataclass
class VirtualObject:
    """Represents a virtual object with its properties"""
    name: str
    index: int  # Index in the virtual objects list
    engagement_level: int  # 0=low, 1=medium, 2=high
    involvement_type: str  # grasp, contact, substrate

@dataclass
class PhysicalObject:
    """Represents a physical object with its properties"""
    name: str
    object_id: int
    image_id: int
    index: int  # Index in the physical objects list

@dataclass
class Assignment:
    """Represents an assignment of virtual objects to physical objects"""
    assignment_matrix: np.ndarray  # Binary matrix X[i,j] where i=virtual, j=physical
    virtual_to_physical: Dict[int, int]  # Maps virtual object index to physical object index
    total_loss: float
    loss_components: Dict[str, float]

class ProXeekOptimizer:
    """Global optimization for haptic proxy assignment"""
    
    def __init__(self, data_dir: str = "Editor/Script_py/output"):
        self.data_dir = data_dir
        self.virtual_objects: List[VirtualObject] = []
        self.physical_objects: List[PhysicalObject] = []
        self.realism_matrix: Optional[np.ndarray] = None  # realism_rating[i,j]

        self.interaction_matrix: Optional[np.ndarray] = None  # interaction_rating[j,k] for physical objects
        self.interaction_exists: Optional[np.ndarray] = None  # interaction_exists[i,k] for virtual objects
        
        # Loss function weights
        self.w_realism = 1.0
        self.w_priority = 1.0
        self.w_interaction = 1.0
        
        # Constraints
        self.enable_exclusivity = True  # Each physical object used at most once
        
    def load_data(self) -> bool:
        """Load all required data files"""
        try:
            # Load haptic annotation data - find the most recent haptic_annotation file
            haptic_dir = "StreamingAssets/Export"
            haptic_files = [f for f in os.listdir(haptic_dir) if f.startswith("haptic_annotation") and f.endswith(".json")]
            
            if not haptic_files:
                raise FileNotFoundError(f"No haptic annotation files found in {haptic_dir}")
            
            # Use the most recent file (sorted alphabetically, which works for timestamp format)
            haptic_file = os.path.join(haptic_dir, sorted(haptic_files)[-1])
            print(f"Loading haptic annotation file: {haptic_file}")
            
            with open(haptic_file, 'r') as f:
                haptic_data = json.load(f)
            
            # Load physical object database
            physical_file = os.path.join(self.data_dir, "physical_object_database.json")
            with open(physical_file, 'r') as f:
                physical_data = json.load(f)
            
            # Load proxy matching results (for realism ratings)
            proxy_file = os.path.join(self.data_dir, "proxy_matching_results.json")
            with open(proxy_file, 'r') as f:
                proxy_data = json.load(f)
            
            # Load relationship rating results (for interaction ratings)
            relationship_file = os.path.join(self.data_dir, "relationship_rating_results.json")
            with open(relationship_file, 'r') as f:
                relationship_data = json.load(f)
            
            # Process the loaded data
            self._process_virtual_objects(haptic_data)
            self._process_physical_objects(physical_data)
            self._build_realism_matrix(proxy_data)
            self._build_interaction_matrices(haptic_data, relationship_data)
            
            print(f"Loaded data successfully:")
            print(f"  Virtual objects: {len(self.virtual_objects)}")
            print(f"  Physical objects: {len(self.physical_objects)}")
            if self.realism_matrix is not None:
                print(f"  Realism matrix shape: {self.realism_matrix.shape}")
            if self.interaction_matrix is not None:
                print(f"  Interaction matrix shape: {self.interaction_matrix.shape}")
            
            return True
            
        except Exception as e:
            print(f"Error loading data: {e}")
            import traceback
            traceback.print_exc()
            return False
    
    def _process_virtual_objects(self, haptic_data: Dict) -> None:
        """Process virtual objects from haptic annotation data"""
        node_annotations = haptic_data.get("nodeAnnotations", [])
        high_engagement = haptic_data.get("highEngagementOrder", [])
        medium_engagement = haptic_data.get("mediumEngagementOrder", [])
        low_engagement = haptic_data.get("lowEngagementOrder", [])
        
        # Create complete priority order for granular engagement levels
        complete_priority_order = high_engagement + medium_engagement + low_engagement
        
        # Only include grasp and contact objects (not substrate-only objects)
        filtered_objects = [obj for obj in node_annotations 
                          if obj.get("involvementType") in ["grasp", "contact", "substrate"]]
        
        for i, obj in enumerate(filtered_objects):
            name = obj.get("objectName", "")
            involvement_type = obj.get("involvementType", "")
            
            # Determine granular engagement level (unique rank from 1 to N)
            engagement_level = 0  # default for unranked objects
            if name in complete_priority_order:
                # Higher priority gets higher engagement level
                priority_rank = complete_priority_order.index(name)
                engagement_level = len(complete_priority_order) - priority_rank
            
            virtual_obj = VirtualObject(
                name=name,
                index=i,
                engagement_level=engagement_level,
                involvement_type=involvement_type
            )
            self.virtual_objects.append(virtual_obj)
        
        # Print priority assignments for debugging
        print(f"Priority weights assigned:")
        for virtual_obj in self.virtual_objects:
            print(f"  {virtual_obj.name}: {virtual_obj.engagement_level:.1f}")
    
    def _process_physical_objects(self, physical_data: Dict) -> None:
        """Process physical objects from database"""
        index = 0
        for image_id_str, objects in physical_data.items():
            for obj in objects:
                physical_obj = PhysicalObject(
                    name=obj.get("object", ""),
                    object_id=obj.get("object_id", -1),
                    image_id=obj.get("image_id", int(image_id_str)),
                    index=index
                )
                self.physical_objects.append(physical_obj)
                index += 1
    
    def _build_realism_matrix(self, proxy_data: List[Dict]) -> None:
        """Build the realism rating matrix from proxy matching results"""
        n_virtual = len(self.virtual_objects)
        n_physical = len(self.physical_objects)
        self.realism_matrix = np.zeros((n_virtual, n_physical))
        
        # Create mapping from virtual object name to index
        virtual_name_to_index = {obj.name: obj.index for obj in self.virtual_objects}
        
        # Create mapping from (object_id, image_id) to physical object index
        physical_id_to_index = {(obj.object_id, obj.image_id): obj.index 
                               for obj in self.physical_objects}
        
        for proxy_result in proxy_data:
            virtual_name = proxy_result.get("virtualObject", "")
            object_id = proxy_result.get("object_id", -1)
            image_id = proxy_result.get("image_id", -1)
            rating_score = proxy_result.get("rating_score", 0.0)
            
            if virtual_name in virtual_name_to_index:
                virtual_idx = virtual_name_to_index[virtual_name]
                
                if (object_id, image_id) in physical_id_to_index:
                    physical_idx = physical_id_to_index[(object_id, image_id)]
                    self.realism_matrix[virtual_idx, physical_idx] = rating_score
    
    def _build_interaction_matrices(self, haptic_data: Dict, relationship_data: List[Dict]) -> None:
        """Build interaction matrices from relationship rating data"""
        n_virtual = len(self.virtual_objects)
        n_physical = len(self.physical_objects)
        
        # Initialize interaction_exists matrix for virtual objects
        self.interaction_exists = np.zeros((n_virtual, n_virtual))
        
        # Build virtual object name to index mapping
        virtual_name_to_index = {obj.name: obj.index for obj in self.virtual_objects}
        
        # Mark which virtual objects have interactions
        relationship_annotations = haptic_data.get("relationshipAnnotations", [])
        for rel in relationship_annotations:
            contact_name = rel.get("contactObject", "")
            substrate_name = rel.get("substrateObject", "")
            
            if (contact_name in virtual_name_to_index and 
                substrate_name in virtual_name_to_index):
                contact_idx = virtual_name_to_index[contact_name]
                substrate_idx = virtual_name_to_index[substrate_name]
                self.interaction_exists[contact_idx, substrate_idx] = 1.0
                # Note: This is directional (contact -> substrate)
        
        # Initialize interaction rating matrix for physical objects
        self.interaction_matrix = np.zeros((n_physical, n_physical))
        
        # Build physical object (object_id, image_id) to index mapping
        physical_id_to_index = {(obj.object_id, obj.image_id): obj.index 
                               for obj in self.physical_objects}
        
        # Process relationship rating data to build interaction matrix
        for rel_result in relationship_data:
            contact_obj_id = rel_result.get("contactObject_id", -1)
            contact_img_id = rel_result.get("contactImage_id", -1)
            substrate_obj_id = rel_result.get("substrateObject_id", -1)
            substrate_img_id = rel_result.get("substrateImage_id", -1)
            
            # Calculate mean rating across the three dimensions
            harmony_rating = rel_result.get("harmony_rating", 0)
            expressivity_rating = rel_result.get("expressivity_rating", 0)
            realism_rating = rel_result.get("realism_rating", 0)
            
            mean_rating = (harmony_rating + expressivity_rating + realism_rating) / 3.0
            
            # Map to physical object indices
            contact_key = (contact_obj_id, contact_img_id)
            substrate_key = (substrate_obj_id, substrate_img_id)
            
            if (contact_key in physical_id_to_index and 
                substrate_key in physical_id_to_index):
                contact_phys_idx = physical_id_to_index[contact_key]
                substrate_phys_idx = physical_id_to_index[substrate_key]
                self.interaction_matrix[contact_phys_idx, substrate_phys_idx] = mean_rating
    
    def calculate_realism_loss(self, assignment_matrix: np.ndarray) -> float:
        """Calculate L_realism = -∑ᵢ∑ⱼ (realism_rating[i,j] × X[i,j])"""
        if self.realism_matrix is None:
            return 0.0
        return -np.sum(self.realism_matrix * assignment_matrix)
    
    def calculate_priority_loss(self, assignment_matrix: np.ndarray) -> float:
        """Calculate L_priority = -∑ᵢ (priority[i] × max_realism_for_object[i] × assigned_realism[i])"""
        if self.realism_matrix is None:
            return 0.0
            
        loss = 0.0
        n_virtual = len(self.virtual_objects)
        
        for i in range(n_virtual):
            # Get max realism rating for this virtual object
            max_realism = np.max(self.realism_matrix[i, :])
            
            # Get assigned realism rating
            assigned_realism = np.sum(self.realism_matrix[i, :] * assignment_matrix[i, :])
            
            # Use engagement_level from VirtualObject directly instead of priority_weights array
            priority_weight = self.virtual_objects[i].engagement_level
            
            # Calculate priority loss component
            priority_component = priority_weight * max_realism * assigned_realism
            loss -= priority_component
        
        return loss
    
    def calculate_interaction_loss(self, assignment_matrix: np.ndarray) -> float:
        """Calculate L_interaction = -∑ᵢ∑ₖ (interaction_exists[i,k] × interaction_rating[proxy_assigned[i], proxy_assigned[k]])"""
        if self.interaction_exists is None or self.interaction_matrix is None:
            return 0.0
            
        loss = 0.0
        n_virtual = len(self.virtual_objects)
        
        for i in range(n_virtual):
            for k in range(n_virtual):
                if self.interaction_exists[i, k] > 0:
                    # Find assigned physical objects for virtual objects i and k
                    proxy_i = np.argmax(assignment_matrix[i, :])  # Physical object assigned to virtual object i
                    proxy_k = np.argmax(assignment_matrix[k, :])  # Physical object assigned to virtual object k
                    
                    # Get interaction rating between these physical objects
                    interaction_rating = self.interaction_matrix[proxy_i, proxy_k]
                    loss -= interaction_rating
        
        return loss
    
    def calculate_total_loss(self, assignment_matrix: np.ndarray) -> Tuple[float, Dict[str, float]]:
        """Calculate the total multi-objective loss function"""
        l_realism = self.calculate_realism_loss(assignment_matrix)
        l_priority = self.calculate_priority_loss(assignment_matrix)
        l_interaction = self.calculate_interaction_loss(assignment_matrix)
        
        total_loss = (self.w_realism * l_realism + 
                     self.w_priority * l_priority + 
                     self.w_interaction * l_interaction)
        
        loss_components = {
            "L_realism": l_realism,
            "L_priority": l_priority,
            "L_interaction": l_interaction,
            "total": total_loss
        }
        
        return total_loss, loss_components
    
    def is_valid_assignment(self, assignment_matrix: np.ndarray) -> bool:
        """Check if assignment satisfies hard constraints"""
        # Hard constraint 1: Each virtual object gets exactly one proxy
        row_sums = np.sum(assignment_matrix, axis=1)
        if not np.allclose(row_sums, 1.0):
            return False
        
        # Hard constraint 2: Exclusivity (if enabled)
        if self.enable_exclusivity:
            col_sums = np.sum(assignment_matrix, axis=0)
            if np.any(col_sums > 1.0):
                return False
        
        return True
    
    def generate_all_assignments(self) -> List[np.ndarray]:
        """Generate all valid assignment permutations"""
        n_virtual = len(self.virtual_objects)
        n_physical = len(self.physical_objects)
        
        if n_physical < n_virtual:
            print(f"Error: Not enough physical objects ({n_physical}) for virtual objects ({n_virtual})")
            return []
        
        valid_assignments = []
        
        if self.enable_exclusivity:
            # Generate permutations: each virtual object gets a unique physical object
            for perm in itertools.permutations(range(n_physical), n_virtual):
                assignment_matrix = np.zeros((n_virtual, n_physical))
                for i, j in enumerate(perm):
                    assignment_matrix[i, j] = 1.0
                valid_assignments.append(assignment_matrix)
        else:
            # Generate all combinations: each virtual object can get any physical object
            for assignment_tuple in itertools.product(range(n_physical), repeat=n_virtual):
                assignment_matrix = np.zeros((n_virtual, n_physical))
                for i, j in enumerate(assignment_tuple):
                    assignment_matrix[i, j] = 1.0
                valid_assignments.append(assignment_matrix)
        
        print(f"Generated {len(valid_assignments)} valid assignments")
        return valid_assignments
    
    def optimize(self) -> Optional[Assignment]:
        """Find the optimal assignment with minimum loss"""
        print("Starting global optimization...")
        
        # Generate all possible assignments
        all_assignments = self.generate_all_assignments()
        if not all_assignments:
            return None
        
        best_assignment = None
        best_loss = float('inf')
        best_components = None
        
        print(f"Evaluating {len(all_assignments)} assignments...")
        start_time = time.time()
        
        for i, assignment_matrix in enumerate(all_assignments):
            if i % 1000 == 0 and i > 0:
                elapsed = time.time() - start_time
                print(f"  Processed {i}/{len(all_assignments)} assignments ({elapsed:.1f}s)")
            
            # Verify assignment is valid (should be by construction, but double-check)
            if not self.is_valid_assignment(assignment_matrix):
                continue
            
            # Calculate loss
            total_loss, loss_components = self.calculate_total_loss(assignment_matrix)
            
            # Update best assignment if this is better
            if total_loss < best_loss:
                best_loss = total_loss
                best_components = loss_components
                
                # Create virtual-to-physical mapping
                virtual_to_physical = {}
                for virtual_idx in range(len(self.virtual_objects)):
                    physical_idx = np.argmax(assignment_matrix[virtual_idx, :])
                    virtual_to_physical[virtual_idx] = physical_idx
                
                best_assignment = Assignment(
                    assignment_matrix=assignment_matrix.copy(),
                    virtual_to_physical=virtual_to_physical,
                    total_loss=total_loss,
                    loss_components=loss_components
                )
        
        elapsed = time.time() - start_time
        print(f"Optimization completed in {elapsed:.2f}s")
        print(f"Best total loss: {best_loss:.4f}")
        print(f"Loss components: {best_components}")
        
        return best_assignment
    
    def print_assignment_details(self, assignment: Assignment) -> None:
        """Print detailed information about an assignment"""
        print("\n" + "="*60)
        print("OPTIMAL HAPTIC PROXY ASSIGNMENT")
        print("="*60)
        
        print(f"\nTotal Loss: {assignment.total_loss:.4f}")
        print("Loss Components:")
        for component, value in assignment.loss_components.items():
            print(f"  {component}: {value:.4f}")
        
        print(f"\nVirtual Object Assignments:")
        print("-" * 40)
        for virtual_idx, physical_idx in assignment.virtual_to_physical.items():
            virtual_obj = self.virtual_objects[virtual_idx]
            physical_obj = self.physical_objects[physical_idx]
            
            if self.realism_matrix is not None:
                realism_score = self.realism_matrix[virtual_idx, physical_idx]
            else:
                realism_score = 0.0
                
            # Use engagement_level from VirtualObject directly
            priority = virtual_obj.engagement_level
            
            print(f"{virtual_obj.name} -> {physical_obj.name}")
            print(f"  Priority: {priority:.1f}")
            print(f"  Realism Score: {realism_score:.3f}")
            print(f"  Physical ID: {physical_obj.object_id}, Image: {physical_obj.image_id}")
            print()
    
    def save_results(self, assignment: Assignment, output_file: str = "optimization_results.json") -> None:
        """Save optimization results to JSON file"""
        results = {
            "optimization_summary": {
                "total_loss": assignment.total_loss,
                "loss_components": assignment.loss_components,
                "num_virtual_objects": len(self.virtual_objects),
                "num_physical_objects": len(self.physical_objects),
                "exclusivity_enabled": self.enable_exclusivity,
                "loss_weights": {
                    "w_realism": self.w_realism,
                    "w_priority": self.w_priority,
                    "w_interaction": self.w_interaction
                }
            },
            "assignments": []
        }
        
        # Add detailed assignment information
        for virtual_idx, physical_idx in assignment.virtual_to_physical.items():
            virtual_obj = self.virtual_objects[virtual_idx]
            physical_obj = self.physical_objects[physical_idx]
            
            if self.realism_matrix is not None:
                realism_score = self.realism_matrix[virtual_idx, physical_idx]
            else:
                realism_score = 0.0
                
            # Use engagement_level from VirtualObject directly
            priority_weight = float(virtual_obj.engagement_level)
            
            assignment_info = {
                "virtual_object": {
                    "name": virtual_obj.name,
                    "index": virtual_obj.index,
                    "engagement_level": virtual_obj.engagement_level,
                    "involvement_type": virtual_obj.involvement_type,
                    "priority_weight": priority_weight
                },
                "physical_object": {
                    "name": physical_obj.name,
                    "object_id": physical_obj.object_id,
                    "image_id": physical_obj.image_id,
                    "index": physical_obj.index
                },
                "realism_score": float(realism_score),
                "assignment_matrix_row": assignment.assignment_matrix[virtual_idx, :].tolist()
            }
            results["assignments"].append(assignment_info)
        
        # Save to file
        output_path = os.path.join(self.data_dir, output_file)
        with open(output_path, 'w') as f:
            json.dump(results, f, indent=2)
        
        print(f"\nResults saved to: {output_path}")

def main():
    """Main function to run the optimization"""
    print("ProXeek Global Optimization")
    print("="*40)
    
    # Initialize optimizer
    optimizer = ProXeekOptimizer()
    
    # Load data
    if not optimizer.load_data():
        print("Failed to load data. Exiting.")
        return
    
    # Set loss function weights (can be adjusted)
    optimizer.w_realism = 1.0
    optimizer.w_priority = 0.5
    optimizer.w_interaction = 0.3
    
    # Enable/disable exclusivity constraint
    optimizer.enable_exclusivity = True
    
    print(f"\nOptimization Parameters:")
    print(f"  Realism weight: {optimizer.w_realism}")
    print(f"  Priority weight: {optimizer.w_priority}")
    print(f"  Interaction weight: {optimizer.w_interaction}")
    print(f"  Exclusivity constraint: {optimizer.enable_exclusivity}")
    
    # Run optimization
    best_assignment = optimizer.optimize()
    
    if best_assignment:
        # Print results
        optimizer.print_assignment_details(best_assignment)
        
        # Save results
        optimizer.save_results(best_assignment)
    else:
        print("Optimization failed - no valid assignment found.")

if __name__ == "__main__":
    main() 