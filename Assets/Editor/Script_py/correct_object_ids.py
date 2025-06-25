#!/usr/bin/env python3
"""
Standalone script to correct object IDs in relationship rating results.

This script reads the existing relationship_rating_results.json file and the 
physical_object_database.json file, then corrects any incorrect object IDs 
by matching object names with their correct IDs from the database.

Usage:
    python correct_object_ids.py [--input INPUT_FILE] [--output OUTPUT_FILE] [--database DATABASE_FILE]
"""

import json
import os
import argparse
import difflib
from typing import List, Dict, Any, Tuple, Optional


def log(message: str):
    """Simple logging function"""
    print(f"[LOG] {message}")


def correct_object_ids_in_relationship_results(relationship_results: List[Dict], physical_object_database: Dict) -> List[Dict]:
    """
    Corrects the object IDs in relationship rating results by looking up the correct IDs
    based on object names and image IDs from the physical object database.
    Uses fuzzy matching to handle slight variations in object names.
    
    Args:
        relationship_results: List of relationship rating result dictionaries
        physical_object_database: The physical object database containing correct ID mappings
    
    Returns:
        List of corrected relationship rating results
    """
    corrected_results = []
    
    # Create a lookup dictionary for quick ID resolution
    # Format: {image_id: {object_name: object_id}}
    object_lookup = {}
    
    for image_id_str, objects in physical_object_database.items():
        image_id = int(image_id_str)
        object_lookup[image_id] = {}
        
        for obj in objects:
            object_name = obj.get("object", "").strip().lower()
            object_id = obj.get("object_id")
            if object_name and object_id is not None:
                object_lookup[image_id][object_name] = object_id
    
    def find_best_match_object_id(target_name: str, image_id: int, threshold: float = 0.8) -> Tuple[Optional[int], float]:
        """
        Find the best matching object ID using fuzzy string matching.
        
        Args:
            target_name: The object name to match
            image_id: The image ID to search within
            threshold: Minimum similarity threshold (0.0 to 1.0)
        
        Returns:
            Tuple of (object_id, similarity_score) or (None, 0) if no good match
        """
        if image_id not in object_lookup:
            return None, 0
        
        target_name_clean = target_name.strip().lower()
        
        # First try exact match
        if target_name_clean in object_lookup[image_id]:
            return object_lookup[image_id][target_name_clean], 1.0
        
        # Try fuzzy matching
        best_match = None
        best_score = 0
        
        for db_name, db_id in object_lookup[image_id].items():
            similarity = difflib.SequenceMatcher(None, target_name_clean, db_name).ratio()
            if similarity > best_score and similarity >= threshold:
                best_match = db_id
                best_score = similarity
        
        return best_match, best_score
    
    # Track corrections made
    corrections_made = 0
    
    # Process each relationship result
    for result in relationship_results:
        corrected_result = result.copy()  # Create a copy to avoid modifying the original
        
        # Correct contact object ID
        contact_object_name = result.get("physicalContactObject", "")
        contact_image_id = result.get("contactImage_id")
        
        if contact_object_name and contact_image_id is not None:
            correct_contact_id, similarity = find_best_match_object_id(contact_object_name, contact_image_id)
            if correct_contact_id is not None:
                old_id = result.get("contactObject_id")
                if old_id != correct_contact_id:
                    corrected_result["contactObject_id"] = correct_contact_id
                    corrections_made += 1
                    if similarity < 1.0:
                        log(f"Corrected contact object ID with fuzzy match (similarity: {similarity:.2f}): '{contact_object_name}' in image {contact_image_id} from {old_id} to {correct_contact_id}")
                    else:
                        log(f"Corrected contact object ID: '{contact_object_name}' in image {contact_image_id} from {old_id} to {correct_contact_id}")
            else:
                log(f"Warning: Could not find correct ID for contact object '{contact_object_name}' in image {contact_image_id}")
        
        # Correct substrate object ID
        substrate_object_name = result.get("physicalSubstrateObject", "")
        substrate_image_id = result.get("substrateImage_id")
        
        if substrate_object_name and substrate_image_id is not None:
            correct_substrate_id, similarity = find_best_match_object_id(substrate_object_name, substrate_image_id)
            if correct_substrate_id is not None:
                old_id = result.get("substrateObject_id")
                if old_id != correct_substrate_id:
                    corrected_result["substrateObject_id"] = correct_substrate_id
                    corrections_made += 1
                    if similarity < 1.0:
                        log(f"Corrected substrate object ID with fuzzy match (similarity: {similarity:.2f}): '{substrate_object_name}' in image {substrate_image_id} from {old_id} to {correct_substrate_id}")
                    else:
                        log(f"Corrected substrate object ID: '{substrate_object_name}' in image {substrate_image_id} from {old_id} to {correct_substrate_id}")
            else:
                log(f"Warning: Could not find correct ID for substrate object '{substrate_object_name}' in image {substrate_image_id}")
        
        corrected_results.append(corrected_result)
    
    log(f"Total corrections made: {corrections_made}")
    return corrected_results


def main():
    """Main function to handle command line arguments and execute the correction process"""
    parser = argparse.ArgumentParser(description="Correct object IDs in relationship rating results")
    parser.add_argument("--input", "-i", 
                        default="Editor/Script_py/output/relationship_rating_results.json",
                        help="Input file path (default: Editor/Script_py/output/relationship_rating_results.json)")
    parser.add_argument("--output", "-o", 
                        default="Editor/Script_py/output/relationship_rating_results_corrected.json",
                        help="Output file path (default: Editor/Script_py/output/relationship_rating_results_corrected.json)")
    parser.add_argument("--database", "-d",
                        default="Editor/Script_py/output/physical_object_database.json",
                        help="Physical object database file path (default: Editor/Script_py/output/physical_object_database.json)")
    parser.add_argument("--overwrite", action="store_true", default=True,
                        help="Overwrite the original input file (default: True)")
    parser.add_argument("--preserve", action="store_true",
                        help="Preserve original file and create a new corrected file")
    
    args = parser.parse_args()
    
    # Determine output file
    if args.preserve:
        output_file = args.output
        log(f"Will create corrected file: {output_file}")
        log(f"Original file will be preserved: {args.input}")
    else:
        output_file = args.input
        log(f"Will overwrite original file: {output_file}")
    
    # Check if input files exist
    if not os.path.exists(args.input):
        log(f"Error: Input file '{args.input}' not found!")
        return 1
    
    if not os.path.exists(args.database):
        log(f"Error: Database file '{args.database}' not found!")
        return 1
    
    try:
        # Load the relationship rating results
        log(f"Loading relationship rating results from: {args.input}")
        with open(args.input, 'r', encoding='utf-8') as f:
            relationship_results = json.load(f)
        
        log(f"Loaded {len(relationship_results)} relationship rating results")
        
        # Load the physical object database
        log(f"Loading physical object database from: {args.database}")
        with open(args.database, 'r', encoding='utf-8') as f:
            physical_object_database = json.load(f)
        
        # Print database summary
        total_objects = sum(len(objects) for objects in physical_object_database.values())
        log(f"Loaded physical object database with {len(physical_object_database)} images and {total_objects} total objects")
        
        # Correct the object IDs
        log("Starting object ID correction process...")
        corrected_results = correct_object_ids_in_relationship_results(relationship_results, physical_object_database)
        
        # Create output directory if it doesn't exist
        output_dir = os.path.dirname(output_file)
        if output_dir and not os.path.exists(output_dir):
            os.makedirs(output_dir, exist_ok=True)
            log(f"Created output directory: {output_dir}")
        
        # Save the corrected results
        log(f"Saving corrected results to: {output_file}")
        with open(output_file, 'w', encoding='utf-8') as f:
            json.dump(corrected_results, f, indent=2, ensure_ascii=False)
        
        log("Object ID correction completed successfully!")
        
        if args.preserve:
            log(f"Original file preserved: {args.input}")
            log(f"Corrected file saved: {output_file}")
        else:
            log(f"Original file has been updated with corrections: {output_file}")
        
        return 0
        
    except json.JSONDecodeError as e:
        log(f"Error: Invalid JSON in input file - {e}")
        return 1
    except Exception as e:
        log(f"Error: {e}")
        import traceback
        log(traceback.format_exc())
        return 1


if __name__ == "__main__":
    exit(main()) 